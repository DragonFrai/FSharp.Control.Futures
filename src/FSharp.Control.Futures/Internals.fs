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
    /// <summary>
    ///
    /// </summary>
    /// <param name="f"></param>
    /// <param name="root"> Может быть null </param>
    /// <remarks> Может принимать null значение </remarks>
    let inline forEach<'a when 'a:> IIntrusiveNode<'a> and 'a: not struct> ([<InlineIfLambda>] f: 'a -> unit) (root: 'a) =
        let mutable node = root
        while isNotNull node do
            f node
            node <- node.Next

/// Односвязный список, элементы которого являются его же узлами.
/// Может быть полезен для исключения дополнительных аллокаций услов на подобии услов LinkedList.
/// Например, список ожидающих Context или ожидающих значение 'w: Future
[<Struct>]
type IntrusiveList<'a> when 'a :> IIntrusiveNode<'a> and 'a : not struct =
    val mutable internal startNode: 'a
    val mutable internal endNode: 'a
    internal new(init: 'a) = { startNode = init; endNode = init }

type IntrusiveList<'a> when 'a :> IIntrusiveNode<'a> and 'a : not struct with
    static member Create(): IntrusiveList<'a> = IntrusiveList(nullObj)

    static member Single(x: 'a): IntrusiveList<'a> = IntrusiveList(x)

    /// Проверяет список на пустоту
    member this.IsEmpty: bool =
        isNull this.startNode

    /// Добавляет элемент в конец
    member this.PushBack(x: 'a): unit =
        if this.IsEmpty then
            this.startNode <- x
            this.endNode <- x
            x.Next <- nullObj
        else
            this.endNode.Next <- x
            this.endNode <- x

    /// Забирает элемент из начала
    member this.PopFront(): 'a =
        if this.IsEmpty
        then nullObj
        elif refEq this.endNode this.startNode then
            let r = this.startNode
            this.startNode <- nullObj
            this.endNode <- nullObj
            r
        else
            let first = this.startNode
            let second = this.startNode.Next
            this.startNode <- second
            first

    /// Опустошает список и возвращает первую ноду, по которой можно проитерироваться.
    /// Может быть полезно для краткосрочного взятия лока на список.
    /// <remarks> Результат может быть null </remarks>
    member this.Drain(): 'a =
        let root = this.startNode
        this.startNode <- nullObj
        this.endNode <- nullObj
        root

    /// Убирает конкретный узел из списка
    member this.Remove(toRemove: 'a): bool =
        if this.IsEmpty then
            false
        elif refEq this.startNode toRemove then
            if refEq this.startNode this.endNode then
                this.startNode <- nullObj
                this.endNode <- nullObj
            else
                this.startNode <- this.startNode.Next
            true
        elif refEq this.startNode.Next null then
            let rec findParent (childToRemove: obj) (parent: 'a) (child: 'a) =
                if refEq childToRemove child then parent
                elif isNull child.Next then nullObj
                else findParent childToRemove child child.Next
            let parent = findParent toRemove this.startNode this.startNode.Next
            if refEq parent null
            then false
            else
                parent.Next <- parent.Next.Next
                if isNull parent.Next then // ребенок был последней нодой
                    this.endNode <- parent
                true
        else
            false

    member this.ToList(): 'a list =
        let root = this.startNode
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

/// Утилита автоматически обрабатывающая Transit от опрашиваемой футуры.
/// На данный момент, один из бонусов -- обработка переходов в терминальное состояние для завершения и отмены.
/// НЕ обрабатывает переход в терминальное состояние при исключении.
/// (TODO: если try без фактического исключения абсолютно бесплатен, есть смысл включить его сюда)
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
    new (isNotified: bool) =
        let state = if isNotified then NotifyState.N else NotifyState.Zero
        { _sync = SpinLock(false); _state = state; _context = nullObj }

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
