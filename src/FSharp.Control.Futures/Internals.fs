namespace FSharp.Control.Futures.Internals

open System.Diagnostics
open System.Threading
open FSharp.Control.Futures.Types


[<AutoOpen>]
module Utils =

    let inline internal ( ^ ) f x = f x

    let inline refEq (a: obj) (b: obj) = obj.ReferenceEquals(a, b)
    let inline nullObj<'a when 'a : not struct> = Unchecked.defaultof<'a>
    let inline isNull<'a when 'a : not struct> (x: 'a) = refEq x null
    let inline isNotNull<'a when 'a : not struct> (x: 'a) = not (isNull x)

    let inline unreachable () =
        raise (UnreachableException())

//---------------
// IntrusiveList

[<AllowNullLiteral>]
type IIntrusiveNode<'a> when 'a :> IIntrusiveNode<'a> =
    abstract Next: 'a with get, set

[<AllowNullLiteral>]
type IntrusiveNode<'self> when 'self :> IIntrusiveNode<'self>() =
    [<DefaultValue>]
    val mutable next: 'self
    interface IIntrusiveNode<'self> with
        member this.Next
            with get () = this.next
            and set v = this.next <- v

module IntrusiveNode =
    let rec forEach<'a when 'a:> IIntrusiveNode<'a> and 'a: not struct> (f: 'a -> unit) (root: 'a) =
        if isNull root then ()
        else
            f root
            forEach f root.Next

/// Односвязный список, элементы которого являются его же узлами.
/// Может быть полезен для исключения дополнительных аллокаций услов на подобии услов LinkedList.
/// Например, список ожидающих Context или ожидающих значение 'w: Future
[<Struct>]
type IntrusiveList<'a> when 'a :> IIntrusiveNode<'a> and 'a : not struct =
    val mutable internal startNode: 'a
    val mutable internal endNode: 'a
    new(init: 'a) = { startNode = init; endNode = init }

type IntrusiveList =
    static member Create(): IntrusiveList<'a> = IntrusiveList(nullObj)

    static member Single(x: 'a): IntrusiveList<'a> = IntrusiveList(x)

    /// Проверяет список на пустоту
    static member IsEmpty(list: IntrusiveList<'a> inref): bool =
        isNull list.startNode

    /// Добавляет элемент в конец
    static member PushBack(list: IntrusiveList<'a> byref, x: 'a): unit =
        let mutable list = &list
        if IntrusiveList.IsEmpty(&list) then
            list.startNode <- x
            list.endNode <- x
            x.Next <- nullObj
        else
            list.endNode.Next <- x
            list.endNode <- x

    /// Забирает элемент из начала
    static member PopFront(list: IntrusiveList<'a> byref): 'a =
        let mutable list = &list
        if IntrusiveList.IsEmpty(&list)
        then nullObj
        elif list.endNode = list.startNode then
            let r = list.startNode
            list.startNode <- nullObj
            list.endNode <- nullObj
            r
        else
            let first = list.startNode
            let second = list.startNode.Next
            list.startNode <- second
            first

    /// Опустошает список и возвращает первую ноду, по которой можно проитерироваться.
    /// Может быть полезно для краткосрочного взятия лока на список.
    static member Drain(list: IntrusiveList<'a> byref): 'a =
        let mutable list = &list
        let root = list.startNode
        list.startNode <- nullObj
        list.endNode <- nullObj
        root

    /// Убирает конкретный узел из списка
    static member Remove(list: IntrusiveList<'a> byref, toRemove: 'a): bool =
        let mutable list = &list
        if IntrusiveList.IsEmpty(&list) then
            false
        elif refEq list.startNode toRemove then
            if refEq list.startNode list.endNode then
                list.startNode <- nullObj
                list.endNode <- nullObj
            else
                list.startNode <- list.startNode.Next
            true
        elif refEq list.startNode.Next null then
            let rec findParent (childToRemove: obj) (parent: 'a) (child: 'a) =
                if refEq childToRemove child then parent
                elif isNull child.Next then nullObj
                else findParent childToRemove child child.Next
            let parent = findParent toRemove list.startNode list.startNode.Next
            if refEq parent null
            then false
            else
                parent.Next <- parent.Next.Next
                if isNull parent.Next then // ребенок был последней нодой
                    list.endNode <- parent
                true
        else
            false

    static member ToList(list: IntrusiveList<'a> inref): 'a list =
        let root = list.startNode
        let rec collect (c: 'a list) (node: 'a) =
            if isNull node then c
            else collect (c @ [node]) node.Next
        collect [] root

// IntrusiveList
//---------------
// Helpers

[<AutoOpen>]
type Helpers =

    // NOTE: This method has very strange call syntax:
    // ```
    // PollTransiting(^fut, ctx
    // , onReady=fun x ->
    //     doSmthOnReady x
    // , onPending=fun () ->
    //     doSmthOnPending ()
    // )
    // ```
    // but it's library only helper, so it's ok
    static member inline PollTransiting(fut: _ byref, ctx, [<InlineIfLambda>] onReady, [<InlineIfLambda>] onPending) =
        let mutable doLoop = true
        let mutable result = Unchecked.defaultof<'b>
        while doLoop do
            let p = Future.poll ctx fut
            match p with
            | Poll.Ready x ->
                result <- onReady x
                doLoop <- false
            | Poll.Pending ->
                result <- onPending ()
                doLoop <- false
            | Poll.Transit f ->
                fut <- f
        result

[<AutoOpen>]
module Helpers =
    let inline pollTransiting
        (fut: Future<'a>) (ctx: IContext)
        ([<InlineIfLambda>] onReady: 'a -> 'b)
        ([<InlineIfLambda>] onPending: unit -> 'b)
        ([<InlineIfLambda>] onTransitAction: Future<'a> -> unit)
        : 'b =
        let rec pollTransiting fut =
            let p = Future.poll ctx fut
            match p with
            | Poll.Ready x -> onReady x
            | Poll.Pending -> onPending ()
            | Poll.Transit f ->
                onTransitAction f
                pollTransiting f
        pollTransiting fut

    let inline cancelIfNotNull (fut: Future<'a>) =
        if isNotNull fut then fut.Cancel()

// Helpers
// ---------
// NaivePoll

type [<Struct; RequireQualifiedAccess>]
    NaivePoll<'a> =
    | Ready of result: 'a
    | Pending

/// Утилита автоматически обрабатывающая переход в терминальное состояние для завершения и отмены.
/// НЕ обрабатывает переход в терминальное состояние при исключении
[<Struct>]
type NaivePoller<'a> =
    val mutable public Internal: Future<'a>
    new(fut: Future<'a>) = { Internal = fut }

    member inline this.IsTerminated: bool = isNull this.Internal
    member inline this.Terminate() : unit = this.Internal <- nullObj

    member inline this.Poll(ctx: IContext) : NaivePoll<'a> =
        let mutable result = Unchecked.defaultof<_>
        let mutable makePoll = true
        while makePoll do
            match this.Internal.Poll(ctx) with
            | Poll.Ready r ->
                result <- NaivePoll.Ready r
                makePoll <- false
                this.Internal <- nullObj
            | Poll.Pending ->
                result <- NaivePoll.Pending
                makePoll <- false
            | Poll.Transit transitTo ->
                this.Internal <- transitTo
        result

    member inline this.Cancel() : unit =
        this.Internal.Cancel()
        this.Internal <- nullObj

// NaivePoll
// -----------
// PrimaryMerge

[<Struct>]
type PrimaryMerge<'a, 'b> =
    val mutable _poller1: NaivePoller<'a>
    val mutable _poller2: NaivePoller<'b>
    val mutable _result1: 'a
    val mutable _result2: 'b
    val mutable _resultsBits: int // bitflag: r1 = 1 | r2 = 2

    new (fut1: Future<'a>, fut2: Future<'b>) =
        { _poller1 = NaivePoller(fut1)
          _poller2 = NaivePoller(fut2)
          _result1 = Unchecked.defaultof<_>
          _result2 = Unchecked.defaultof<_>
          _resultsBits = 0 }

    member inline this._PutResult1(r: 'a) =
        this._result1 <- r
        this._resultsBits <- this._resultsBits ||| 0b01

    member inline this._PutResult2(r: 'b) =
        this._result2 <- r
        this._resultsBits <- this._resultsBits ||| 0b10

    member inline this._IsNoResult1 = this._resultsBits &&& 0b01 = 0
    member inline this._IsNoResult2 = this._resultsBits &&& 0b10 = 0
    member inline this._HasAllResults = this._resultsBits = 0b11

    member inline this.Poll(ctx: IContext) : NaivePoll<struct ('a * 'b)> =
        if this._IsNoResult1 then
            try
                match this._poller1.Poll(ctx) with
                | NaivePoll.Ready result -> this._PutResult1(result)
                | NaivePoll.Pending -> ()
            with ex ->
                this._poller1.Terminate()
                this._poller2.Cancel()
                raise ex
        if this._IsNoResult2 then
            try
                match this._poller2.Poll(ctx) with
                | NaivePoll.Ready result -> this._PutResult2(result)
                | NaivePoll.Pending -> ()
            with ex ->
                this._poller2.Terminate()
                this._poller1.Cancel()
                raise ex

        if this._HasAllResults
        then NaivePoll.Ready (struct (this._result1, this._result2))
        else NaivePoll.Pending

    member inline this.Cancel() =
        // Отсутствие результата также означает, что Future должна быть не терминальна
        if this._IsNoResult1 then this._poller1.Cancel()
        if this._IsNoResult2 then this._poller2.Cancel()

// PrimaryMerge
// ---------------
// PrimaryNotify

exception MultipleNotifyException

[<RequireQualifiedAccess>]
module NotifyState =
    // for operations Poll(P), Cancel(C) and Notify(N) present next transitions on graphiz:
    // digraph G {
    //     Zero -> N [label = N]
    //     W -> N [label = N]
    //     T -> TN [label = N]
    //
    //     Zero -> W [label = P]
    //     N -> TN [label = P]
    //     W -> W [label = P]
    //
    //     Zero -> T [label = C]
    //     N -> TN [label = C]
    //     W -> T [label = C]
    //
    //     // T -> TerminatedEx [label = C]
    //     // TN -> TerminatedEx [label = C]
    //     // T -> TerminatedEx [label = P]
    //     // TN -> TerminatedEx [label = P]
    //     // N -> MultipleNotifyEx [label = N];
    //     // TN -> MultipleNotifyEx [label = N]
    // }

    // T -- Terminated : Poll with Ready / Cancel
    // W -- Waiting : has context
    // N -- Notified
    // Zero -- initial state
    let [<Literal>] Zero = 0
    let [<Literal>] N = 1
    let [<Literal>] W = 2
    let [<Literal>] T = 3
    let [<Literal>] TN = 4

type [<Struct; NoComparison; NoEquality>] PrimaryNotify =
    val _sync: SpinLock
    val mutable _state: int
    val mutable _context: IContext
    internal new (_phantom: uint8) = { _sync = SpinLock(false); _state = 0; _context = nullObj }
    static member Create() : PrimaryNotify = PrimaryNotify(0uy)

    member inline this.Notify() : unit =
        let mutable hasLock = false
        this._sync.Enter(&hasLock)
        match this._state with
        | NotifyState.N
        | NotifyState.TN ->
            if hasLock then this._sync.Exit()
            raise MultipleNotifyException
        | NotifyState.Zero ->
            this._state <- NotifyState.N
            if hasLock then this._sync.Exit()
        | NotifyState.T ->
            this._state <- NotifyState.TN
            if hasLock then this._sync.Exit()
        | NotifyState.W ->
            let context = this._context
            this._context <- nullObj
            this._state <- NotifyState.N
            if hasLock then this._sync.Exit()
            context.Wake()
        | _ -> unreachable ()

    /// true ~ Ready () | false ~ Pending
    member inline this.Poll(ctx: IContext) : bool =
        let mutable hasLock = false
        this._sync.Enter(&hasLock)
        match this._state with
        | NotifyState.T
        | NotifyState.TN ->
            if hasLock then this._sync.Exit()
            raise FutureTerminatedException
        | NotifyState.Zero ->
            this._context <- ctx
            this._state <- NotifyState.W
            if hasLock then this._sync.Exit()
            false
        | NotifyState.W ->
            if hasLock then this._sync.Exit()
            false
        | NotifyState.N ->
            this._state <- NotifyState.TN
            if hasLock then this._sync.Exit()
            true
        | _ -> unreachable ()

    member inline this.Cancel() =
        let mutable hasLock = false
        this._sync.Enter(&hasLock)
        match this._state with
        | NotifyState.T
        | NotifyState.TN ->
            if hasLock then this._sync.Exit()
            raise FutureTerminatedException
        | NotifyState.Zero ->
            this._state <- NotifyState.T
            if hasLock then this._sync.Exit()
        | NotifyState.W ->
            this._context <- nullObj
            this._state <- NotifyState.T
            if hasLock then this._sync.Exit()
        | NotifyState.N ->
            this._state <- NotifyState.TN
            if hasLock then this._sync.Exit()
        | _ -> unreachable ()

// PrimaryNotify
// ----------------
