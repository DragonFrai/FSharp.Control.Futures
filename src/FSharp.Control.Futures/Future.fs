namespace FSharp.Control.Futures

open System.ComponentModel

// Contains the basic functions for creating and transforming `Computation`.
// If the function accepts types other than `Computation` or `Context`, then they should be placed somewhere else

/// <summary> Current state of a Computation </summary>
[<Struct; RequireQualifiedAccess>]
type Poll<'a> =
    | Ready of 'a
    | Pending

[<RequireQualifiedAccess>]
module Poll =

    let inline isReady x =
        match x with
        | Poll.Ready _ -> true
        | Poll.Pending -> false

    let inline isPending x =
        match x with
        | Poll.Ready _ -> false
        | Poll.Pending -> true

    let inline onReady (f: 'a -> unit) (x: Poll<'a>) : unit =
        match x with
        | Poll.Ready x -> f x
        | Poll.Pending -> ()

    let inline bind (binder: 'a -> Poll<'b>) (x: Poll<'a>): Poll<'b> =
        match x with
        | Poll.Ready x -> binder x
        | Poll.Pending -> Poll.Pending

    let inline bindPending (binder: unit -> Poll<'a>) (x: Poll<'a>): Poll<'a> =
        match x with
        | Poll.Ready x -> Poll.Ready x
        | Poll.Pending -> binder ()

    let inline map (f: 'a -> 'b) (x: Poll<'a>) : Poll<'b> =
        match x with
        | Poll.Ready x -> Poll.Ready (f x)
        | Poll.Pending -> Poll.Pending

/// <summary> The context of the running computation.
/// Allows the computation to signal its ability to move forward (awake) through the Wake method </summary>
[<AbstractClass>]
type Context() =
    abstract Wake: unit -> unit

/// # Future poll schema
/// [ Poll.Pending -> ...(may be infinite)... -> Poll.Pending ] -> Poll.Ready x1 -> ... -> Poll.Ready xn
///  x1 == x2 == ... == xn
[<Interface>]
type IComputation<'a> =
    /// <summary> Poll the state </summary>
    /// <param name="context"> Current Computation context </param>
    /// <returns> Current state </returns>
    [<EditorBrowsable(EditorBrowsableState.Advanced)>]
    abstract Poll: context: Context -> Poll<'a>

    /// <summary> Cancel asynchronously Computation computation </summary>
    /// <remarks> Notifies internal asynchronous operations of Computation cancellations. It is useless if Computation is cold.  </remarks>
    [<EditorBrowsable(EditorBrowsableState.Advanced)>]
    abstract Cancel: unit -> unit

[<RequireQualifiedAccess>]
module Computation =

    let inline cancelNullable (comp: IComputation<'a>) =
        if isNotNull comp then comp.Cancel()

    let inline cancel (comp: IComputation<'a>) =
        comp.Cancel()

    /// <summary> Create a Computation with members from passed functions </summary>
    /// <param name="__expand_poll"> Poll body </param>
    /// <param name="__expand_cancel"> Poll body </param>
    /// <returns> Computation implementations with passed members </returns>
    let inline create (__expand_poll: Context -> Poll<'a>) (__expand_cancel: unit -> unit) : IComputation<'a> =
        { new IComputation<'a> with
            member this.Poll(context) = __expand_poll context
            member this.Cancel() = __expand_cancel () }

    /// <summary> Create a Computation memoizing the first <code>Ready x</code> value
    /// with members from passed functions </summary>
    /// <param name="__expand_poll"> Poll body </param>
    /// <param name="__expand_cancel"> Poll body </param>
    /// <returns> Computation implementations with passed members </returns>
    let inline createMemo (__expand_poll: Context -> Poll<'a>) (__expand_cancel: unit -> unit) : IComputation<'a> =
        let mutable hasResult = false; // 0 -- pending; 1 -- with value
        let mutable result: 'a = Unchecked.defaultof<_>
        create
        <| fun ctx ->
            if hasResult then
                Poll.Ready result
            else
                let p = __expand_poll ctx
                match p with
                | Poll.Pending -> Poll.Pending
                | Poll.Ready x ->
                    result <- x
                    hasResult <- true
                    Poll.Ready x
        <| __expand_cancel

    let inline poll context (comp: IComputation<'a>) = comp.Poll(context)


    /// <summary> Create the Computation with ready value</summary>
    /// <param name="value"> Poll body </param>
    /// <returns> Computation returned <code>Ready value</code> when polled </returns>
    let ready value =
        create
        <| fun _ -> Poll.Ready value
        <| fun () -> do ()

    /// <summary> Create the Computation returned <code>Ready ()</code> when polled</summary>
    /// <returns> Computation returned <code>Ready ()value)</code> when polled </returns>
    let unit =
        create
        <| fun _ -> Poll.Ready ()
        <| fun () -> do ()

    /// <summary> Creates always pending Computation </summary>
    /// <returns> always pending Computation </returns>
    let never<'a> : IComputation<'a> =
        create
        <| fun _ -> Poll<'a>.Pending
        <| fun () -> do ()

    /// <summary> Creates the Computation lazy evaluator for the passed function </summary>
    /// <returns> Computation lazy evaluator for the passed function </returns>
    let lazy' (f: unit -> 'a) : IComputation<'a> =
        createMemo
        <| fun _ -> Poll.Ready (f ())
        <| fun () -> do ()

    /// <summary> Creates the Computation, asynchronously applies the result of the passed compure to the binder </summary>
    /// <returns> Computation, asynchronously applies the result of the passed compure to the binder </returns>
    let bind (binder: 'a -> IComputation<'b>) (comp: IComputation<'a>) : IComputation<'b> =
        // let binder = binder
        let mutable _compA = comp
        let mutable _compB = nullObj

        create
        <| fun context ->
            if isNull _compB then
                match poll context _compA with
                | Poll.Ready x ->
                    _compB <- binder x
                    // binder <- nullObj
                    _compA <- nullObj
                    poll context _compB
                | Poll.Pending -> Poll.Pending
            else
                poll context _compB
        <| fun () ->
            cancelNullable _compA
            cancelNullable _compB

    /// <summary> Creates the Computation, asynchronously applies mapper to result passed Computation </summary>
    /// <returns> Computation, asynchronously applies mapper to result passed Computation </returns>
    let map (mapping: 'a -> 'b) (comp: IComputation<'a>) : IComputation<'b> =
        let mutable _comp = comp // _comp = null, when memoized
        //let mutable _mapping = mapping // _mapping = null, when memoized
        let mutable _value = Unchecked.defaultof<_>

        create
        <| fun context ->
            if isNull _comp then
                Poll.Ready _value
            else
                match _comp.Poll(context) with
                | Poll.Pending -> Poll.Pending
                | Poll.Ready x ->
                    let r = mapping x
                    _value <- r
                    _comp <- Unchecked.defaultof<_>
                    Poll.Ready r
        <| fun () -> cancelNullable _comp

    /// <summary> Creates the Computation, asynchronously merging the results of passed Computations </summary>
    /// <remarks> If one of the Computations threw an exception, the same exception will be thrown everywhere,
    /// and the other Computations will be canceled </remarks>
    /// <returns> Computation, asynchronously merging the results of passed Computation </returns>
    let merge (comp1: IComputation<'a>) (comp2: IComputation<'b>) : IComputation<'a * 'b> =

        let mutable _exn = Unchecked.defaultof<_>
        let mutable _comp1 = comp1 // if null -- has _r1
        let mutable _comp2 = comp2 // if null -- has _r2
        let mutable _r1 = Unchecked.defaultof<_>
        let mutable _r2 = Unchecked.defaultof<_>

        let inline onExn exn =
            _exn <- exn
            _comp1 <- Unchecked.defaultof<_>
            _comp2 <- Unchecked.defaultof<_>
            _r1 <- Unchecked.defaultof<_>
            _r2 <- Unchecked.defaultof<_>

        create
        <| fun ctx ->
            if isNull _exn // if has not exception
            then
                if isNotNull _comp1 then
                    try
                        poll ctx _comp1
                        |> Poll.onReady (fun x ->
                            _comp1 <- Unchecked.defaultof<_>
                            _r1 <- x)
                    with
                    | exn ->
                        cancelNullable _comp2
                        onExn exn
                        raise exn

                if isNotNull _comp2 then
                    try
                        poll ctx _comp2
                        |> Poll.onReady (fun x ->
                            _comp2 <- Unchecked.defaultof<_>
                            _r2 <- x)
                    with
                    | exn ->
                        cancelNullable _comp1
                        onExn exn
                        raise exn

                if (isNull _comp1) && (isNull _comp2)
                    then Poll.Ready (_r1, _r2)
                    else Poll.Pending
            else
                raise _exn
        <| fun () ->
            cancelNullable _comp1
            cancelNullable _comp2

    /// <summary> Creates a Computations that will return the result of
    /// the first one that pulled out the result from the passed  </summary>
    /// <remarks> If one of the Computations threw an exception, the same exception will be thrown everywhere,
    /// and the other Computations will be canceled </remarks>
    /// <returns> Computation, asynchronously merging the results of passed Computation </returns>
    let first (comp1: IComputation<'a>) (comp2: IComputation<'a>) : IComputation<'a> =

        let mutable _exn = Unchecked.defaultof<_>
        let mutable _comp1 = comp1 // if null -- has _r
        let mutable _comp2 = comp2 // if null -- has _r
        let mutable _r = Unchecked.defaultof<_>

        let inline onExn exn =
            _exn <- exn
            _comp1 <- Unchecked.defaultof<_>
            _comp2 <- Unchecked.defaultof<_>
            _r <- Unchecked.defaultof<_>

        create
        <| fun ctx ->
            if isNull _exn // if has not exception
            then
                if isNull _comp1
                then Poll.Ready _r
                else
                    let pollR =
                        try
                            poll ctx _comp1
                        with
                        | exn ->
                            cancelNullable _comp2
                            onExn exn
                            raise exn
                    match pollR with
                    | Poll.Ready x ->
                        _comp2.Cancel()
                        _comp1 <- Unchecked.defaultof<_>
                        _comp2 <- Unchecked.defaultof<_>
                        _r <- x
                        Poll.Ready x
                    | Poll.Pending ->
                        let pollR =
                            try
                                poll ctx _comp2
                            with
                            | exn ->
                                cancelNullable _comp1
                                onExn exn
                                raise exn
                        match pollR with
                            | Poll.Ready x ->
                                _comp1.Cancel()
                                _comp1 <- Unchecked.defaultof<_>
                                _comp2 <- Unchecked.defaultof<_>
                                _r <- x
                                Poll.Ready x
                            | Poll.Pending ->
                                Poll.Pending
            else
                raise _exn
        <| fun () ->
            cancelNullable _comp1
            cancelNullable _comp2

    /// <summary> Creates the Computation, asynchronously applies 'f' function to result passed Computation </summary>
    /// <returns> Computation, asynchronously applies 'f' function to result passed Computation </returns>
    let apply (f: IComputation<'a -> 'b>) (comp: IComputation<'a>) : IComputation<'b> =
        let mutable _fnFut = f // null when fn was got
        let mutable _sourceFut = comp // null when 'a was got
        let mutable _fn = Unchecked.defaultof<_>
        let mutable _value = Unchecked.defaultof<_>

        // Memoize the result so as not to call Apply twice
        createMemo
        <| fun context ->
            if isNotNull _fnFut then
                poll context _fnFut
                |> (Poll.onReady <| fun x ->
                    _fnFut <- nullObj
                    _fn <- x)
            if isNotNull _sourceFut then
                poll context _sourceFut
                |> (Poll.onReady <| fun x ->
                    _sourceFut <- nullObj
                    _value <- x)
            if (isNull _fnFut) && (isNull _sourceFut) then
                Poll.Ready (_fn _value)
            else
                Poll.Pending
        <| fun () ->
            cancelNullable _fnFut
            cancelNullable _sourceFut

    /// <summary> Creates the Computation, asynchronously joining the result of passed Computation </summary>
    /// <returns> Computation, asynchronously joining the result of passed Computation </returns>
    let join (comp: IComputation<IComputation<'a>>) : IComputation<'a> =
        // _inner == null до дожидания _source
        // _inner != null после дожидания _source
        let mutable _source = comp //
        let mutable _inner = Unchecked.defaultof<_> //
        create
        <| fun context ->
            if isNotNull _inner then
                poll context _inner
            else
                let sourcePoll = poll context _source
                match sourcePoll with
                | Poll.Ready inner ->
                    _inner <- inner
                    _source <- Unchecked.defaultof<_>
                    poll context inner
                | Poll.Pending -> Poll.Pending
        <| fun () ->
            cancelNullable _source
            cancelNullable _inner

    /// <summary> Create a Computation delaying invocation and computation of the Computation of the passed creator </summary>
    /// <returns> Computation delaying invocation and computation of the Computation of the passed creator </returns>
    let delay (creator: unit -> IComputation<'a>) : IComputation<'a> =
        // Фьюча с задержкой её инстанцирования.
        // Когда _inner == null, то фьюча еще не инициализирована
        //
        let mutable _inner: IComputation<'a> = Unchecked.defaultof<_>
        create
        <| fun context ->
            if isNotNull _inner
            then poll context _inner
            else
                let inner = creator ()
                _inner <- inner
                poll context inner
        <| fun () ->
            cancelNullable _inner

    /// <summary> Creates a Computation that returns control flow to the scheduler once </summary>
    /// <returns> Computation that returns control flow to the scheduler once </returns>
    let yieldWorkflow () =
        let mutable isYielded = false
        create
        <| fun context ->
            if isYielded then
                Poll.Ready ()
            else
                isYielded <- true
                context.Wake()
                Poll.Pending
        <| fun () -> do ()

    /// <summary> Creates a Computation that ignore result of the passed Computation </summary>
    /// <returns> Computation that ignore result of the passed Computation </returns>
    let ignore comp =
        create
        <| fun context ->
            match poll context comp with
            | Poll.Ready _ -> Poll.Ready ()
            | Poll.Pending -> Poll.Pending
        <| fun () -> do comp.Cancel()


[<Struct; NoComparison; NoEquality>]
type Future<'a> =
    { Raw: unit -> IComputation<'a> }
    //member inline this.Raw = this.Raw

[<RequireQualifiedAccess>]
module Future =

    /// <summary> Получает внутренний unit -> Computation. </summary>
    /// <remarks> Может содержать результат Delay из билдера и вычисление, которое должно выполняться асинхронно
    /// Эта функция не должна вызываться вне асинхронного контекста </remarks>
    let inline raw (fut: Future<'a>) = fut.Raw

    /// <summary> Создает внутренний Computation. </summary>
    let inline runRaw (fut: Future<'a>) = fut.Raw ()

    let inline create (__expand_creator: unit -> IComputation<'a>) : Future<'a> = { Future.Raw = __expand_creator }

    /// <summary> Create the Future with ready value</summary>
    /// <param name="value"> Poll body </param>
    /// <returns> Future returned <code>Ready value</code> when polled </returns>
    let inline ready value =
        create (fun () -> Computation.ready value)

    /// <summary> Create the Future returned <code>Ready ()</code> when polled</summary>
    /// <returns> Future returned <code>Ready ()value)</code> when polled </returns>
    let unit =
        create (fun () -> Computation.unit)

    /// <summary> Creates always pending Future </summary>
    /// <returns> always pending Future </returns>
    let inline never<'a> : Future<'a> =
        create (fun () -> Computation.never<'a>)

    /// <summary> Creates the Future lazy evaluator for the passed function </summary>
    /// <returns> Future lazy evaluator for the passed function </returns>
    let inline lazy' f =
        create (fun () -> Computation.lazy' f)

    /// <summary> Creates the Future, asynchronously applies the result of the passed future to the binder </summary>
    /// <returns> Future, asynchronously applies the result of the passed future to the binder </returns>
    let inline bind binder fut =
        create (fun () -> Computation.bind (binder >> runRaw) (runRaw fut) )

    /// <summary> Creates the Future, asynchronously applies mapper to result passed Computation </summary>
    /// <returns> Future, asynchronously applies mapper to result passed Computation </returns>
    let inline map mapping fut =
        create (fun () -> Computation.map mapping (runRaw fut))

    /// <summary> Creates the Future, asynchronously applies 'f' function to result passed Computation </summary>
    /// <returns> Future, asynchronously applies 'f' function to result passed Computation </returns>
    let inline apply f fut =
        create (fun () -> Computation.apply (runRaw f) (runRaw fut))

    /// <summary> Creates the Future, asynchronously merging the results of passed Future </summary>
    /// <remarks> If one of the Computations threw an exception, the same exception will be thrown everywhere,
    /// and the other Future will be canceled </remarks>
    /// <returns> Future, asynchronously merging the results of passed Future </returns>
    let inline merge fut1 fut2 =
        create (fun () -> Computation.merge (runRaw fut1) (runRaw fut2))

    /// <summary> Creates a Future that will return the result of
    /// the first one that pulled out the result from the passed  </summary>
    /// <remarks> If one of the Future threw an exception, the same exception will be thrown everywhere,
    /// and the other Future will be canceled </remarks>
    /// <returns> Future, asynchronously merging the results of passed Future </returns>
    let inline first fut1 fut2 =
        create (fun () -> Computation.first (runRaw fut1) (runRaw fut2))

    /// <summary> Creates the Future, asynchronously joining the result of passed Computation </summary>
    /// <returns> Future, asynchronously joining the result of passed Computation </returns>
    let inline join fut =
        create (fun () -> Computation.join (runRaw (map runRaw fut)))

    /// <summary> Creates a Future that returns control flow to the scheduler once </summary>
    /// <returns> Future that returns control flow to the scheduler once </returns>
    let yieldWorkflow = create Computation.yieldWorkflow

    /// <summary> Creates a Future that ignore result of the passed Computation </summary>
    /// <returns> Future that ignore result of the passed Computation </returns>
    let inline ignore fut =
        create (fun () -> Computation.ignore (runRaw fut))

