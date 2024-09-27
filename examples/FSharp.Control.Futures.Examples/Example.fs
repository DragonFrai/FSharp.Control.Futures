module FSharp.Control.Futures.Playground.Example
//
//open System.ComponentModel
//
//
//[<Struct; RequireQualifiedAccess>]
//type Poll<'a> =
//    | Ready of 'a
//    | Pending
//
//[<RequireQualifiedAccess>]
//module Poll =
//    let inline onReady (f: 'a -> unit) (x: Poll<'a>) : unit =
//        match x with
//        | Poll.Ready x -> f x
//        | Poll.Pending -> ()
//
//    let inline bind (binder: 'a -> Poll<'b>) (x: Poll<'a>): Poll<'b> =
//        match x with
//        | Poll.Ready x -> binder x
//        | Poll.Pending -> Poll.Pending
//
//    let inline bindPending (binder: unit -> Poll<'a>) (x: Poll<'a>): Poll<'a> =
//        match x with
//        | Poll.Ready x -> Poll.Ready x
//        | Poll.Pending -> binder ()
//
//    let inline map (f: 'a -> 'b) (x: Poll<'a>) : Poll<'b> =
//        match x with
//        | Poll.Ready x -> Poll.Ready (f x)
//        | Poll.Pending -> Poll.Pending
//
//[<AbstractClass>]
//type Context() =
//    abstract Wake: unit -> unit
//
///// # Future poll schema
///// [ Poll.Pending -> ...(may be infinite)... -> Poll.Pending ] -> Poll.Ready x1 -> ... -> Poll.Ready xn
/////  x1 == x2 == ... == xn
//[<Interface>]
//type IComputation<'a> =
//    [<EditorBrowsable(EditorBrowsableState.Advanced)>]
//    abstract Poll: Context -> Poll<'a>
//    [<EditorBrowsable(EditorBrowsableState.Advanced)>]
//    abstract Cancel: unit -> unit
//
//[<RequireQualifiedAccess>]
//module Computation =
//
//    let inline cancelNullable (comp: IComputation<'a>) =
//        if isNotNull comp then comp.Cancel()
//
//    let inline cancel (comp: IComputation<'a>) =
//        comp.Cancel()
//
//    let inline create (__expand_poll: Context -> Poll<'a>) (__expand_cancel: unit -> unit) : IComputation<'a> =
//        { new IComputation<'a> with
//            member this.Poll(context) = __expand_poll context
//            member this.Cancel() = __expand_cancel () }
//
//    let inline createMemo (__expand_poll: Context -> Poll<'a>) (__expand_cancel: unit -> unit) : IComputation<'a> =
//        let mutable hasResult = false; // 0 -- pending; 1 -- with value
//        let mutable result: 'a = Unchecked.defaultof<_>
//        create
//        <| fun ctx ->
//            if hasResult then
//                Poll.Ready result
//            else
//                let p = __expand_poll ctx
//                match p with
//                | Poll.Pending -> Poll.Pending
//                | Poll.Ready x ->
//                    result <- x
//                    hasResult <- true
//                    Poll.Ready x
//        <| __expand_cancel
//
//    let inline poll context (comp: IComputation<'a>) = comp.Poll(context)
//
//
//
//    let ready value =
//        create
//        <| fun _ -> Poll.Ready value
//        <| fun () -> do ()
//
//    let unit =
//        create
//        <| fun _ -> Poll.Ready ()
//        <| fun () -> do ()
//
//    let never<'a> : IComputation<'a> =
//        create
//        <| fun _ -> Poll<'a>.Pending
//        <| fun () -> do ()
//
//    let lazy' (f: unit -> 'a) : IComputation<'a> =
//        createMemo
//        <| fun _ -> Poll.Ready (f ())
//        <| fun () -> do ()
//
//    let bind (binder: 'a -> IComputation<'b>) (comp: IComputation<'a>) : IComputation<'b> =
//        // let binder = binder
//        let mutable _compA = comp
//        let mutable _compB = nullObj
//
//        create
//        <| fun context ->
//            if isNull _compB then
//                match Computation.poll context _compA with
//                | Poll.Ready x ->
//                    _compB <- binder x
//                    // binder <- nullObj
//                    _compA <- nullObj
//                    Computation.poll context _compB
//                | Poll.Pending -> Poll.Pending
//            else
//                Computation.poll context _compB
//        <| fun () ->
//            cancelNullable _compA
//            cancelNullable _compB
//
//    let map (mapping: 'a -> 'b) (comp: IComputation<'a>) : IComputation<'b> =
//        let mutable _comp = comp // _comp = null, when memoized
//        //let mutable _mapping = mapping // _mapping = null, when memoized
//        let mutable _value = Unchecked.defaultof<_>
//
//        create
//        <| fun context ->
//            if isNull _comp then
//                Poll.Ready _value
//            else
//                match _comp.Poll(context) with
//                | Poll.Pending -> Poll.Pending
//                | Poll.Ready x ->
//                    let r = mapping x
//                    _value <- r
//                    _comp <- Unchecked.defaultof<_>
//                    Poll.Ready r
//        <| fun () -> cancelNullable _comp
//
//    let merge (comp1: IComputation<'a>) (comp2: IComputation<'b>) : IComputation<'a * 'b> =
//
//        let mutable _exn = Unchecked.defaultof<_>
//        let mutable _comp1 = comp1 // if null -- has _r1
//        let mutable _comp2 = comp2 // if null -- has _r2
//        let mutable _r1 = Unchecked.defaultof<_>
//        let mutable _r2 = Unchecked.defaultof<_>
//
//        let inline onExn exn =
//            _exn <- exn
//            _comp1 <- Unchecked.defaultof<_>
//            _comp2 <- Unchecked.defaultof<_>
//            _r1 <- Unchecked.defaultof<_>
//            _r2 <- Unchecked.defaultof<_>
//
//        create
//        <| fun ctx ->
//            if isNull _exn // if has not exception
//            then
//                if isNotNull _comp1 then
//                    try
//                        Computation.poll ctx _comp1
//                        |> Poll.onReady (fun x ->
//                            _comp1 <- Unchecked.defaultof<_>
//                            _r1 <- x)
//                    with
//                    | exn ->
//                        cancelNullable _comp2
//                        onExn exn
//                        raise exn
//
//                if isNotNull _comp2 then
//                    try
//                        Computation.poll ctx _comp2
//                        |> Poll.onReady (fun x ->
//                            _comp2 <- Unchecked.defaultof<_>
//                            _r2 <- x)
//                    with
//                    | exn ->
//                        cancelNullable _comp1
//                        onExn exn
//                        raise exn
//
//                if (isNull _comp1) && (isNull _comp2)
//                    then Poll.Ready (_r1, _r2)
//                    else Poll.Pending
//            else
//                raise _exn
//        <| fun () ->
//            cancelNullable _comp1
//            cancelNullable _comp2
//
//    let first (comp1: IComputation<'a>) (comp2: IComputation<'a>) : IComputation<'a> =
//
//        let mutable _exn = Unchecked.defaultof<_>
//        let mutable _comp1 = comp1 // if null -- has _r
//        let mutable _comp2 = comp2 // if null -- has _r
//        let mutable _r = Unchecked.defaultof<_>
//
//        let inline onExn exn =
//            _exn <- exn
//            _comp1 <- Unchecked.defaultof<_>
//            _comp2 <- Unchecked.defaultof<_>
//            _r <- Unchecked.defaultof<_>
//
//        create
//        <| fun ctx ->
//            if isNull _exn // if has not exception
//            then
//                if isNull _comp1
//                then Poll.Ready _r
//                else
//                    let poll =
//                        try
//                            Computation.poll ctx _comp1
//                        with
//                        | exn ->
//                            cancelNullable _comp2
//                            onExn exn
//                            raise exn
//                    match poll with
//                    | Poll.Ready x ->
//                        _comp2.Cancel()
//                        _comp1 <- Unchecked.defaultof<_>
//                        _comp2 <- Unchecked.defaultof<_>
//                        _r <- x
//                        Poll.Ready x
//                    | Poll.Pending ->
//                        let poll =
//                            try
//                                Computation.poll ctx _comp2
//                            with
//                            | exn ->
//                                cancelNullable _comp1
//                                onExn exn
//                                raise exn
//                        match poll with
//                            | Poll.Ready x ->
//                                _comp1.Cancel()
//                                _comp1 <- Unchecked.defaultof<_>
//                                _comp2 <- Unchecked.defaultof<_>
//                                _r <- x
//                                Poll.Ready x
//                            | Poll.Pending ->
//                                Poll.Pending
//            else
//                raise _exn
//        <| fun () ->
//            cancelNullable _comp1
//            cancelNullable _comp2
//
//    let apply (f: IComputation<'a -> 'b>) (comp: IComputation<'a>) : IComputation<'b> =
//        let mutable _fnFut = f // null when fn was got
//        let mutable _sourceFut = comp // null when 'a was got
//        let mutable _fn = Unchecked.defaultof<_>
//        let mutable _value = Unchecked.defaultof<_>
//
//        // Memoize the result so as not to call Apply twice
//        createMemo
//        <| fun context ->
//            if isNotNull _fnFut then
//                Computation.poll context _fnFut
//                |> (Poll.onReady <| fun x ->
//                    _fnFut <- nullObj
//                    _fn <- x)
//            if isNotNull _sourceFut then
//                Computation.poll context _sourceFut
//                |> (Poll.onReady <| fun x ->
//                    _sourceFut <- nullObj
//                    _value <- x)
//            if (isNull _fnFut) && (isNull _sourceFut) then
//                Poll.Ready (_fn _value)
//            else
//                Poll.Pending
//        <| fun () ->
//            cancelNullable _fnFut
//            cancelNullable _sourceFut
//
//    let join (comp: IComputation<IComputation<'a>>) : IComputation<'a> =
//        // _inner == null до дожидания _source
//        // _inner != null после дожидания _source
//        let mutable _source = comp //
//        let mutable _inner = Unchecked.defaultof<_> //
//        create
//        <| fun context ->
//            if isNotNull _inner then
//                Computation.poll context _inner
//            else
//                let sourcePoll = Computation.poll context _source
//                match sourcePoll with
//                | Poll.Ready inner ->
//                    _inner <- inner
//                    _source <- Unchecked.defaultof<_>
//                    Computation.poll context inner
//                | Poll.Pending -> Poll.Pending
//        <| fun () ->
//            cancelNullable _source
//            cancelNullable _inner
//
//    let delay (creator: unit -> IComputation<'a>) : IComputation<'a> =
//        // Фьюча с задержкой её инстанцирования.
//        // Когда _inner == null, то фьюча еще не инициализирована
//        //
//        let mutable _inner: IComputation<'a> = Unchecked.defaultof<_>
//        create
//        <| fun context ->
//            if isNotNull _inner
//            then poll context _inner
//            else
//                let inner = creator ()
//                _inner <- inner
//                poll context inner
//        <| fun () ->
//            cancelNullable _inner
//
//    let yieldWorkflow () =
//        let mutable isYielded = false
//        Computation.create
//        <| fun context ->
//            if isYielded then
//                Poll.Ready ()
//            else
//                isYielded <- true
//                context.Wake()
//                Poll.Pending
//        <| fun () -> do ()
//
//    let ignore comp =
//        create
//        <| fun context ->
//            match Computation.poll context comp with
//            | Poll.Ready _ -> Poll.Ready ()
//            | Poll.Pending -> Poll.Pending
//        <| fun () -> do comp.Cancel()
