namespace rec FSharp.Control.Futures

open System.ComponentModel


[<Struct; RequireQualifiedAccess>]
type Poll<'a> =
    | Ready of 'a
    | Pending

[<RequireQualifiedAccess>]
module Poll =
    let inline onReady (f: 'a -> unit) (x: Poll<'a>) : unit =
        match x with
        | Poll.Ready x -> f x
        | Poll.Pending -> ()

[<AbstractClass>]
type Context() =
    abstract Wake: unit -> unit

/// # Future poll schema
/// [ Poll.Pending -> ...(may be infinite)... -> Poll.Pending ] -> Poll.Ready x1 -> ... -> Poll.Ready xn
///  x1 == x2 == ... == xn
[<Interface>]
type IFuture<'a> =
    [<EditorBrowsable(EditorBrowsableState.Advanced)>]
    abstract Poll: Context -> Poll<'a>
    [<EditorBrowsable(EditorBrowsableState.Advanced)>]
    abstract Cancel: unit -> unit

// I know, I know
type Future<'a> = IFuture<'a>

[<RequireQualifiedAccess>]
module Future =

    [<RequireQualifiedAccess>]
    module Core =

        let inline cancelNullable (fut: Future<'a>) =
            if isNotNull fut then fut.Cancel()

        let inline create (__expand_poll: Context -> Poll<'a>) (__expand_cancel: (unit -> unit)) : Future<'a> =
            { new Future<'a> with
                member this.Poll(context) = __expand_poll context
                member this.Cancel() = __expand_cancel () }

        let inline createMemo (__expand_poll: Context -> Poll<'a>) (__expand_cancel: (unit -> unit)) : Future<'a> =
            let mutable hasResult = false; // 0 -- pending; 1 -- with value
            let mutable result: 'a = Unchecked.defaultof<_>
            Core.create
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

        let inline poll context (fut: Future<'a>) = fut.Poll(context)


    let inline bindPoll' (f: 'a -> Poll<'b>) (x: Poll<'a>) : Poll<'b> =
        match x with
        | Poll.Ready x -> f x
        | Poll.Pending -> Poll.Pending

    let ready value =
        Core.create
        <| fun _ -> Poll.Ready value
        <| fun () -> do ()

    let unit =
        Core.create
        <| fun _ -> Poll.Ready ()
        <| fun () -> do ()

    let never<'a> : Future<'a> =
        Core.create
        <| fun _ -> Poll<'a>.Pending
        <| fun () -> do ()

    let lazy' (f: unit -> 'a) : Future<'a> =
        Core.createMemo
        <| fun _ -> Poll.Ready (f ())
        <| fun () -> do ()

    let bind (binder: 'a -> Future<'b>) (fut: Future<'a>) : Future<'b> =
        // let binder = binder
        let mutable _futA = fut
        let mutable _futB = nullObj

        Core.create
        <| fun context ->
            if isNull _futB then
                match Future.Core.poll context _futA with
                | Poll.Ready x ->
                    _futB <- binder x
                    // binder <- nullObj
                    _futA <- nullObj
                    Future.Core.poll context _futB
                | Poll.Pending -> Poll.Pending
            else
                Future.Core.poll context _futB
        <| fun () ->
            Core.cancelNullable _futA
            Core.cancelNullable _futB

    let map (mapping: 'a -> 'b) (fut: Future<'a>) : Future<'b> =
        let mutable _fut = fut // _fut = null, when memoized
        //let mutable _mapping = mapping // _mapping = null, when memoized
        let mutable _value = Unchecked.defaultof<_>

        Core.create
        <| fun context ->
            if isNull _fut then
                Poll.Ready _value
            else
                match _fut.Poll(context) with
                | Poll.Pending -> Poll.Pending
                | Poll.Ready x ->
                    let r = mapping x
                    _value <- r
                    _fut <- Unchecked.defaultof<_>
                    Poll.Ready r
        <| fun () -> Core.cancelNullable _fut

    let merge (fut1: Future<'a>) (fut2: Future<'b>) : Future<'a * 'b> =

        let mutable _fut1 = fut1 // if null -- has _r1
        let mutable _fut2 = fut2 // if null -- has _r2
        let mutable _r1 = Unchecked.defaultof<_>
        let mutable _r2 = Unchecked.defaultof<_>

        Core.create
        <| fun context ->
            if isNotNull _fut1 then
                Future.Core.poll context _fut1
                |> (Poll.onReady <| fun x ->
                    _fut1 <- nullObj
                    _r1 <- x)
            if isNotNull _fut2 then
                Future.Core.poll context _fut2
                |> (Poll.onReady <| fun x ->
                    _fut2 <- nullObj
                    _r2 <- x)
            if (isNull _fut1) && (isNull _fut2) then
                Poll.Ready (_r1, _r2)
            else
                Poll.Pending
        <| fun () ->
            Core.cancelNullable _fut1
            Core.cancelNullable _fut2

    let apply (f: Future<'a -> 'b>) (fut: Future<'a>) : Future<'b> =
        let mutable _fnFut = f // null when fn was got
        let mutable _sourceFut = fut // null when 'a was got
        let mutable _fn = Unchecked.defaultof<_>
        let mutable _value = Unchecked.defaultof<_>

        // Memoize the result so as not to call Apply twice
        Core.createMemo
        <| fun context ->
            if isNotNull _fnFut then
                Future.Core.poll context _fnFut
                |> (Poll.onReady <| fun x ->
                    _fnFut <- nullObj
                    _fn <- x)
            if isNotNull _sourceFut then
                Future.Core.poll context _sourceFut
                |> (Poll.onReady <| fun x ->
                    _sourceFut <- nullObj
                    _value <- x)
            if (isNull _fnFut) && (isNull _sourceFut) then
                Poll.Ready (_fn _value)
            else
                Poll.Pending
        <| fun () ->
            Core.cancelNullable _fnFut
            Core.cancelNullable _sourceFut

    let join (fut: Future<Future<'a>>) : Future<'a> =
        // _inner == null до дожидания _source
        // _inner != null после дожидания _source
        let mutable _source = fut //
        let mutable _inner = Unchecked.defaultof<_> //
        Core.create
        <| fun context ->
            if isNotNull _inner then
                Future.Core.poll context _inner
            else
                let sourcePoll = Future.Core.poll context _source
                match sourcePoll with
                | Poll.Ready inner ->
                    _inner <- inner
                    _source <- Unchecked.defaultof<_>
                    Future.Core.poll context inner
                | Poll.Pending -> Poll.Pending
        <| fun () ->
            Core.cancelNullable _source
            Core.cancelNullable _inner

    let delay (creator: unit -> Future<'a>) : Future<'a> =
        // Фьюча с задержкой её инстанцирования.
        // Когда _inner == null, то фьюча еще не инициализирована
        //
        let mutable _inner: Future<'a> = Unchecked.defaultof<_>
        Core.create
        <| fun context ->
            if isNotNull _inner
            then Core.poll context _inner
            else
                let inner = creator ()
                _inner <- inner
                Core.poll context inner
        <| fun () ->
            Core.cancelNullable _inner

    let yieldWorkflow () =
        let mutable isYielded = false
        Future.Core.create
        <| fun context ->
            if isYielded then
                Poll.Ready ()
            else
                isYielded <- true
                context.Wake()
                Poll.Pending
        <| fun () -> do ()

    let ignore fut =
        Core.create
        <| fun context ->
            match Future.Core.poll context fut with
            | Poll.Ready _ -> Poll.Ready ()
            | Poll.Pending -> Poll.Pending
        <| fun () -> do fut.Cancel()
