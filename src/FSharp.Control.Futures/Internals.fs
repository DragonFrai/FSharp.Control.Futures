namespace FSharp.Control.Futures.Internals

open System
open System.Diagnostics
open System.Threading
open FSharp.Control.Futures

[<AutoOpen>]
module Utils =

    let inline internal ( ^ ) f x = f x

    let inline refEq (a: obj) (b: obj) = obj.ReferenceEquals(a, b)
    let inline nullObj<'a when 'a : not struct> = Unchecked.defaultof<'a>
    let inline isNull<'a when 'a : not struct> (x: 'a) = refEq x null
    let inline isNotNull<'a when 'a : not struct> (x: 'a) = not (isNull x)

    let inline unreachable () =
        raise (UnreachableException())


//---------------------------
// Future low level functions

[<RequireQualifiedAccess>]
module Future =

    let inline drop (comp: Future<'a>) = comp.Drop()

    let inline poll context (comp: Future<'a>) = comp.Poll(context)

    let inline create ([<InlineIfLambda>] poll) ([<InlineIfLambda>] drop) =
        { new Future<_> with
            member _.Poll(ctx) = poll ctx
            member _.Drop() = drop () }

// Future low level functions
//---------------------------
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
        if isNotNull fut then fut.Drop()

// Helpers
// --------

// [ EnxResult ]

type [<Struct>] ExnResult<'a>(value: 'a, ex: exn) =
    static member inline Ok(v) = ExnResult(v, Unchecked.defaultof<_>)
    static member inline Exn(e) = ExnResult(Unchecked.defaultof<_>, e)
    member inline _.Value = if isNull ex then value else raise ex

// ---------
// NaivePoll

type [<Struct; RequireQualifiedAccess>]
    NaivePoll<'a> =
    | Ready of result: 'a
    | Pending

module NaivePoll =
    let inline toPoll (naivePoll: NaivePoll<'a>) : Poll<'a> =
        match naivePoll with
        | NaivePoll.Ready result -> Poll.Ready result
        | NaivePoll.Pending -> Poll.Pending

    let inline toPollExn (naivePoll: NaivePoll<ExnResult<'a>>) : Poll<'a> =
        match naivePoll with
        | NaivePoll.Ready result -> Poll.Ready result.Value
        | NaivePoll.Pending -> Poll.Pending


/// Утилита автоматически обрабатывающая Transit от опрашиваемой футуры.
/// На данный момент, один из бонусов -- обработка переходов в терминальное состояние
/// для завершения с результатом или исключением и отмены.
/// (TODO: если try без фактического исключения не абсолютно бесплатен, есть смысл убрать его отсюда)
[<Struct; NoComparison; NoEquality>]
type NaiveFuture<'a> =
    val mutable public Internal: Future<'a>
    new(fut: Future<'a>) = { Internal = fut }

    member inline this.IsTerminated: bool = isNull this.Internal
    member inline this.Terminate() : unit = this.Internal <- nullObj

    member inline this.Poll(ctx: IContext) : NaivePoll<'a> =
        let mutable result = Unchecked.defaultof<_>
        let mutable doLoop = true
        while doLoop do
            let poll =
                try this.Internal.Poll(ctx)
                with e -> this.Terminate(); reraise ()

            match poll with
            | Poll.Ready r ->
                this.Internal <- nullObj
                doLoop <- false; result <- NaivePoll.Ready r
            | Poll.Pending ->
                doLoop <- false; result <- NaivePoll.Pending
            | Poll.Transit transitTo ->
                this.Internal <- transitTo
        result

    member inline this.Drop() : unit =
        this.Internal.Drop()
        this.Internal <- nullObj

// NaivePoll
// -----------
// PrimaryMerge

[<Struct>]
type PrimaryMerge<'a, 'b> =
    val mutable _poller1: NaiveFuture<'a>
    val mutable _poller2: NaiveFuture<'b>
    val mutable _result1: 'a
    val mutable _result2: 'b
    val mutable _resultsBits: int // bitflag: r1 = 1 | r2 = 2

    new (fut1: Future<'a>, fut2: Future<'b>) =
        { _poller1 = NaiveFuture(fut1)
          _poller2 = NaiveFuture(fut2)
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
                this._poller2.Drop()
                raise ex
        if this._IsNoResult2 then
            try
                match this._poller2.Poll(ctx) with
                | NaivePoll.Ready result -> this._PutResult2(result)
                | NaivePoll.Pending -> ()
            with ex ->
                this._poller2.Terminate()
                this._poller1.Drop()
                raise ex

        if this._HasAllResults
        then NaivePoll.Ready (struct (this._result1, this._result2))
        else NaivePoll.Pending

    member inline this.Drop() =
        // Отсутствие результата также означает, что Future должна быть не терминальна
        if this._IsNoResult1 then this._poller1.Drop()
        if this._IsNoResult2 then this._poller2.Drop()

// PrimaryMerge
// ---------------
// PrimaryNotify

exception MultipleNotifyException

[<RequireQualifiedAccess>]
module NotifyState =
    // for operations Poll(P), Cancel(C) and Notify(N) present next transitions on graphiz:
    // digraph G {
    //     I -> N [label = N]
    //     W -> N [label = N]
    //     T -> TN [label = N]
    //
    //     I -> W [label = P]
    //     N -> TN [label = P]
    //     W -> W [label = P]
    //
    //     I -> T [label = C]
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

    // I -- Initialized
    // T -- Terminated: Polled with Ready or Canceled
    // W -- Waiting: has context
    // N -- Notified

    let [<Literal>] I = 0
    let [<Literal>] W = 1
    let [<Literal>] N = 2
    let [<Literal>] T = 3
    let [<Literal>] TN = 4

/// <summary>
/// The primitive for synchronizing ONE notifier and ONE notifiable.
/// SPSC (Single Producer Single Consumer)
/// </summary>
type [<Struct; NoComparison; NoEquality>] PrimaryNotify =
    val mutable _state: int
    val mutable _context: IContext

    new (isNotified: bool) =
        let state = if isNotified then NotifyState.N else NotifyState.I
        { _state = state; _context = nullObj }

    member inline this.IsInitOnly =
        let state = this._state
        state = NotifyState.I

    member inline this.IsWaiting =
        let state = this._state
        state = NotifyState.W

    member inline this.IsNotified =
        let state = this._state
        state = NotifyState.N || state = NotifyState.TN

    member inline this.IsTerminated =
        let state = this._state
        state = NotifyState.T || state = NotifyState.TN

    member inline this.TerminateForInit() =
        if this._state = NotifyState.I then
            this._state <- NotifyState.TN
        else
            raise (InvalidOperationException())

    // TODO: Replace bool to struct DU ?
    /// <summary>
    /// This can be used to undo the effect produced before the notification starts.
    /// For example, remove the previously set value.
    /// </summary>
    /// <returns> true when future already was cancelled (terminated, but ready unreachable without notify) </returns>
    /// <remarks> The return value can be used to manually clean up external resources after notification </remarks>
    member inline this.Notify() : bool =
        let mutable doLoop = true
        let mutable state = this._state
        let mutable result = false
        while doLoop do
            match state with
            | NotifyState.I ->
                let state' = Interlocked.CompareExchange(&this._state, NotifyState.N, state)
                if state <> state' then state <- state'
                else doLoop <- false; result <- false
            | NotifyState.W ->
                let state' = Interlocked.CompareExchange(&this._state, NotifyState.N, state)
                if state <> state' then state <- state'
                else
                    let ctx = this._context
                    this._context <- nullObj
                    ctx.Wake()
                    doLoop <- false; result <- false
            | NotifyState.T ->
                let state' = Interlocked.CompareExchange(&this._state, NotifyState.TN, state)
                if state <> state' then state <- state'
                else doLoop <- false; result <- true
            | NotifyState.N
            | NotifyState.TN -> raise MultipleNotifyException
            | _ -> unreachable ()
        result

    /// <summary>
    /// Polls notify
    /// </summary>
    /// <param name="ctx"> Current async context </param>
    /// <returns> true if notified </returns>
    member inline this.Poll(ctx: IContext) : bool =
        let mutable doLoop = true
        let mutable state = this._state
        let mutable result = false
        while doLoop do
            match state with
            | NotifyState.I ->
                this._context <- ctx
                let state' = Interlocked.CompareExchange(&this._state, NotifyState.W, state)
                if state <> state' then state <- state'
                else doLoop <- false; result <- false
            | NotifyState.W ->
                if this._context <> ctx then unreachable ()
                doLoop <- false; result <- false
            | NotifyState.N ->
                let state' = Interlocked.CompareExchange(&this._state, NotifyState.TN, state)
                if state <> state' then state <- state'
                else doLoop <- false; result <- true
            | NotifyState.T
            | NotifyState.TN -> raise FutureTerminatedException
            | _ -> unreachable ()
        result

    /// <summary>
    /// Cancel waiting notification
    /// </summary>
    /// <returns> true if notified </returns>
    /// <remarks> The return value can be used to manually clean up external resources after cancellation </remarks>
    member inline this.Drop() : bool =
        let mutable doLoop = true
        let mutable result = false
        let mutable state = this._state
        while doLoop do
            match state with
            | NotifyState.I ->
                let state' = Interlocked.CompareExchange(&this._state, NotifyState.T, state)
                if state <> state' then state <- state'
                else doLoop <- false; result <- false
            | NotifyState.W ->
                let state' = Interlocked.CompareExchange(&this._state, NotifyState.T, state)
                if state <> state' then state <- state'
                else
                    this._context <- nullObj
                    doLoop <- false; result <- false
            | NotifyState.N ->
                let state' = Interlocked.CompareExchange(&this._state, NotifyState.TN, state)
                if state <> state' then state <- state'
                else doLoop <- false; result <- true
            | NotifyState.T
            | NotifyState.TN -> raise FutureTerminatedException
            | _ -> unreachable ()
        result

// PrimaryNotify
// ----------------
// PrimaryOnceCell

/// <summary>
/// A primitive for synchronizing ONE value sender and ONE value receiver
/// </summary>
type [<Struct; NoComparison; NoEquality>] PrimaryOnceCell<'a> =
    val mutable _notify: PrimaryNotify
    val mutable _value: 'a

    internal new (notify: PrimaryNotify, value: 'a) = { _notify = notify; _value = value }
    new ((): unit) = { _notify = PrimaryNotify(false); _value = Unchecked.defaultof<'a> }
    static member Empty() = PrimaryOnceCell(PrimaryNotify(false), Unchecked.defaultof<'a>)
    static member WithValue(value: 'a) = PrimaryOnceCell(PrimaryNotify(true), value)

    member inline this.HasValue = this._notify.IsNotified
    member inline this.Value = this._value

    member inline this.Put(value: 'a) =
        this._value <- value
        if this._notify.Notify()
        then this._value <- Unchecked.defaultof<'a>

    member inline this.Poll(ctx: IContext) : NaivePoll<'a> =
        let isNotified = this._notify.Poll(ctx)
        match isNotified with
        | false -> NaivePoll.Pending
        | true -> NaivePoll.Ready this._value

    member inline this.Get() : ValueOption<'a> =
        match this.HasValue with
        | false -> ValueNone
        | true -> ValueSome this._value

    member inline this.Drop() =
        if this._notify.Drop()
        then this._value <- Unchecked.defaultof<'a>

// PrimaryOnceCell
// -----------
// Futures

[<RequireQualifiedAccess>]
module Futures =

    [<Sealed>]
    type Ready<'a>(value: 'a) =
        interface Future<'a> with
            member this.Poll(_ctx) = Poll.Ready value
            member this.Drop() = ()

    [<Sealed>]
    type ReadyUnit internal () =
        static member Instance : ReadyUnit = ReadyUnit()
        interface Future<unit> with
            member this.Poll(_ctx) = Poll.Ready ()
            member this.Drop() = ()

    let ReadyUnit : ReadyUnit = ReadyUnit()

    [<Sealed>]
    type Never<'a> internal () =
        interface Future<'a> with
            member this.Poll(_ctx) = Poll.Pending
            member this.Drop() = ()

    let Never<'a> : Never<'a> = Never()

    [<Sealed>]
    type Lazy<'a>(lazy': unit -> 'a) =
        interface Future<'a> with
            member this.Poll(_ctx) = Poll.Ready (lazy' ())
            member this.Drop() = ()

    [<Sealed>]
    type Delay<'a>(delay: unit -> Future<'a>) =
        interface Future<'a> with
            member this.Poll(_ctx) = Poll.Transit (delay ())
            member this.Drop() = ()

    [<Sealed>]
    type Yield() =
        let mutable isYielded = false
        interface Future<unit> with
            member this.Poll(ctx) =
                if isYielded
                then Poll.Ready ()
                else
                    isYielded <- true
                    ctx.Wake()
                    Poll.Pending
            member this.Drop() = ()

    [<Sealed>]
    type Bind<'a, 'b>(source: Future<'a>, binder: 'a -> Future<'b>) =
        let mutable poller = NaiveFuture(source)
        interface Future<'b> with
            member this.Poll(ctx) =
                match poller.Poll(ctx) with
                | NaivePoll.Ready result -> Poll.Transit (binder result)
                | NaivePoll.Pending -> Poll.Pending

            member this.Drop() =
                poller.Drop()

    [<Sealed>]
    type Map<'a, 'b>(source: Future<'a>, mapping: 'a -> 'b) =
        let mutable poller = NaiveFuture(source)
        interface Future<'b> with
            member this.Poll(ctx) =
                match poller.Poll(ctx) with
                | NaivePoll.Ready result -> Poll.Ready (mapping result)
                | NaivePoll.Pending -> Poll.Pending

            member this.Drop() =
                poller.Drop()

    [<Sealed>]
    type Ignore<'a>(source: Future<'a>) =
        let mutable poller = NaiveFuture(source)
        interface Future<unit> with
            member this.Poll(ctx) =
                match poller.Poll(ctx) with
                | NaivePoll.Ready _ -> Poll.Ready ()
                | NaivePoll.Pending -> Poll.Pending

            member this.Drop() =
                poller.Drop()

    [<Sealed>]
    type Merge<'a, 'b>(fut1: Future<'a>, fut2: Future<'b>) =
        let mutable sMerge = PrimaryMerge(fut1, fut2)

        interface Future<'a * 'b> with
            member this.Poll(ctx) =
                match sMerge.Poll(ctx) with
                | NaivePoll.Ready (struct (a, b)) -> Poll.Ready (a, b)
                | NaivePoll.Pending -> Poll.Pending

            member this.Drop() =
                sMerge.Drop()

    [<Sealed>]
    type First<'a>(fut1: Future<'a>, fut2: Future<'a>) =
        let mutable poller1 = NaiveFuture(fut1)
        let mutable poller2 = NaiveFuture(fut2)

        interface Future<'a> with
            member _.Poll(ctx) =
                try
                    match poller1.Poll(ctx) with
                    | NaivePoll.Ready result ->
                        poller2.Drop()
                        Poll.Ready result
                    | NaivePoll.Pending ->
                        try
                            match poller2.Poll(ctx) with
                            | NaivePoll.Ready result ->
                                poller1.Drop()
                                Poll.Ready result
                            | NaivePoll.Pending ->
                                Poll.Pending
                        with ex ->
                            poller2.Terminate()
                            poller1.Drop()
                            raise ex
                with _ ->
                    poller1.Terminate()
                    poller2.Drop()
                    reraise ()

            member _.Drop() =
                poller1.Drop()
                poller2.Drop()

    [<Sealed>]
    type Join<'a>(source: Future<Future<'a>>) =
        let mutable poller = NaiveFuture(source)
        interface Future<'a> with
            member this.Poll(ctx) =
                match poller.Poll(ctx) with
                | NaivePoll.Ready innerFut -> Poll.Transit innerFut
                | NaivePoll.Pending -> Poll.Pending
            member this.Drop() =
                poller.Drop()

    [<Sealed>]
    type Apply<'a, 'b>(fut: Future<'a>, futFun: Future<'a -> 'b>) =
        let mutable sMerge = PrimaryMerge(fut, futFun)

        interface Future<'b> with
            member _.Poll(ctx) =
                match sMerge.Poll(ctx) with
                | NaivePoll.Ready (struct (x, f)) -> Poll.Ready (f x)
                | NaivePoll.Pending -> Poll.Pending

            member _.Drop() =
                sMerge.Drop()

    [<Sealed>]
    type TryWith<'a>(body: Future<'a>, handler: exn -> Future<'a>) =
        let mutable poller = NaiveFuture(body)
        let mutable handler = handler

        interface Future<'a> with
            member _.Poll(ctx) =
                try
                    match poller.Poll(ctx) with
                    | NaivePoll.Pending -> Poll.Pending
                    | NaivePoll.Ready x ->
                        handler <- nullObj
                        Poll.Ready x
                with ex ->
                    poller.Terminate()
                    let h = handler
                    handler <- nullObj
                    Poll.Transit (h ex)

            member _.Drop() =
                handler <- nullObj
                poller.Drop()

    // TODO: TryFinally<'a>
    // [<Sealed>]
    // type TryFinally<'a>(body: Future<'a>, finalizer: unit -> unit) =

// Futures
// --------


// [Fuse future]
[<AutoOpen>]
module FuseFuture =

    type FutureFuseException(message: string) = inherit Exception(message)
    type FutureFuseReadyException() = inherit FutureFuseException("Future was polled after returning Ready.")
    type FutureFuseCancelledException() = inherit FutureFuseException("Future was polled after being cancelled.")
    type FutureFuseTransitedException() = inherit FutureFuseException("Future was polled after returning Transit.")

    [<RequireQualifiedAccess>]
    module Future =
        type internal Fuse<'a>(fut: Future<'a>) =
            let mutable isReady = false
            let mutable isCancelled = false
            let mutable isTransited = false
            interface Future<'a> with
                member _.Poll(ctx) =
                    if isReady then raise (FutureFuseReadyException())
                    elif isCancelled then raise (FutureFuseCancelledException())
                    elif isTransited then raise (FutureFuseTransitedException())
                    else
                        let p = Future.poll ctx fut
                        match p with
                        | Poll.Pending -> Poll.Pending
                        | Poll.Ready x ->
                            isReady <- true
                            Poll.Ready x
                        | Poll.Transit f ->
                            isTransited <- true
                            Poll.Transit f
                member _.Drop() =
                    isCancelled <- true


        /// <summary>
        /// Creates a Future that will throw a specific <see cref="FutureFuseException">FutureFuseException</see> if polled after returning Ready or Transit or being cancelled.
        /// </summary>
        /// <exception cref="FutureFuseReadyException">Throws if Future was polled after returning Ready.</exception>
        /// <exception cref="FutureFuseTransitedException">Throws if Future was polled after returning Transit.</exception>
        /// <exception cref="FutureFuseCancelledException">Throws if Future was polled after being cancelled.</exception>
        let fuse (fut: Future<'a>) : Future<'a> =
            upcast Fuse<'a>(fut)
