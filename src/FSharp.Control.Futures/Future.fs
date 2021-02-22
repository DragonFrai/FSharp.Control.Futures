namespace rec FSharp.Control.Futures

open System
open System.Threading


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

type Waker = unit -> unit

/// # Future poll schema
/// [ Poll.Pending -> ...(may be infinite)... -> Poll.Pending ] -> Poll.Ready x1 -> ... -> Poll.Ready xn
///  x1 == x2 == ... == xn
[<Interface>]
type IFuture<'a> =
    abstract member Poll: Waker -> Poll<'a>

// I know, I know
type Future<'a> = IFuture<'a>

[<RequireQualifiedAccess>]
module Future =

    [<RequireQualifiedAccess>]
    module Core =

        let inline create (__expand_poll: Waker -> Poll<'a>): Future<'a> =
            { new Future<'a> with member this.Poll(waker) = __expand_poll waker }

        let inline memoizeReady (poll: Waker -> Poll<'a>) : Future<'a> =
            let mutable poll = poll // poll = null, when memoized
            let mutable result: 'a = Unchecked.defaultof<_>
            Core.create ^fun waker ->
                if obj.ReferenceEquals(poll, null) then
                    Poll.Ready result
                else
                    let p = poll waker
                    match p with
                    | Poll.Pending -> Poll.Pending
                    | Poll.Ready x ->
                        result <- x
                        poll <- Unchecked.defaultof<_>
                        Poll.Ready x

        let inline poll waker (fut: Future<'a>) = fut.Poll(waker)

        let getWaker = create Poll.Ready


    let inline bindPoll' (f: 'a -> Poll<'b>) (x: Poll<'a>) : Poll<'b> =
        match x with
        | Poll.Ready x -> f x
        | Poll.Pending -> Poll.Pending

    let ready value = Core.create ^fun _ -> Poll.Ready value

    let unit () = Core.create ^fun _ -> Poll.Ready ()

    let lazy' (f: unit -> 'a) : Future<'a> =
//        let mutable x = Unchecked.defaultof<'a>
//        let mutable func = f
//        Core.create ^fun _ ->
//            if obj.ReferenceEquals(Unchecked.defaultof<_>, func)
//            then Poll.Ready x
//            else
//                x <- func()
//                func <- Unchecked.defaultof<_>
//                Poll.Ready x
        Core.memoizeReady ^fun _ ->
            Poll.Ready (f ())

    let never () : Future<'a> = Core.create ^fun _ -> Poll<'a>.Pending

    let bind (binder: 'a -> Future<'b>) (fut: Future<'a>) : Future<'b> =
        let mutable futA = fut
        let mutable futB = ValueNone
        Core.create ^fun waker ->
            match futB with
            | ValueNone ->
                match Future.Core.poll waker futA with
                     | Poll.Ready x ->
                         let futB' = binder x
                         futB <- ValueSome futB'
                         futA <- Unchecked.defaultof<_>
                         Future.Core.poll waker futB'
                     | Poll.Pending -> Poll.Pending
            | ValueSome futB -> Future.Core.poll waker futB

    let map (mapping: 'a -> 'b) (fut: Future<'a>) : Future<'b> =
        let mutable value = ValueNone
        Core.create ^fun waker ->
            match value with
            | ValueNone ->
                Future.Core.poll waker fut
                |> bindPoll' ^fun x ->
                    let r = mapping x
                    value <- ValueSome r
                    Poll.Ready r
            | ValueSome x -> Poll.Ready x

    let apply (f: Future<'a -> 'b>) (fut: Future<'a>) : Future<'b> =
        let mutable rf = ValueNone
        let mutable r1 = ValueNone
        Core.create ^fun waker ->
            Future.Core.poll waker f |> Poll.onReady ^fun f -> rf <- ValueSome f
            Future.Core.poll waker fut |> Poll.onReady ^fun x1 -> r1 <- ValueSome x1
            match rf, r1 with
            | ValueSome f, ValueSome x1 ->
                Poll.Ready (f x1)
            | _ -> Poll.Pending

    // TODO: rewrite to interlocked
    let merge (fut1: Future<'a>) (fut2: Future<'b>) : Future<'a * 'b> =
        let nullWaker: Waker = Unchecked.defaultof<_>

        let syncObj = obj()

        let mutable fut1 = fut1
        let mutable fut2 = fut2

        // when future is Poll.Pending and current waker = null, then waker already called of other branch
        let mutable currentWaker = nullWaker
        let mutable r1 = ValueNone
        let mutable r2 = ValueNone

        let mutable firstRequirePoll = true
        let mutable secondRequirePoll = true

        let callWaker () =
            if not (obj.ReferenceEquals(currentWaker, nullWaker)) then
                currentWaker ()
            currentWaker <- nullWaker

        let proxyWaker1 = fun () ->
            lock syncObj ^fun () ->
                callWaker ()
                firstRequirePoll <- true

        let proxyWaker2 = fun () ->
            lock syncObj ^fun () ->
                callWaker ()
                secondRequirePoll <- true

        Core.create ^fun waker ->
            lock syncObj ^fun () ->
                currentWaker <- waker

                if firstRequirePoll then
                    firstRequirePoll <- false
                    let x = fut1.Poll(proxyWaker1)
                    match x with
                    | Poll.Pending -> ()
                    | Poll.Ready x ->
                        r1 <- ValueSome x
                        fut1 <- Unchecked.defaultof<_>

                if secondRequirePoll then
                    secondRequirePoll <- false
                    let x = fut2.Poll(proxyWaker2)
                    match x with
                    | Poll.Pending -> ()
                    | Poll.Ready x ->
                        r2 <- ValueSome x
                        fut2 <- Unchecked.defaultof<_>

                match r1, r2 with
                | ValueSome x1, ValueSome x2 -> Poll.Ready (x1, x2)
                | _ -> Poll.Pending

    let join (fut: Future<Future<'a>>) : Future<'a> =
        let mutable inner = ValueNone
        Core.create ^fun waker ->
            if inner.IsNone then
                Future.Core.poll waker fut
                |> Poll.onReady ^fun inner' ->
                    inner <- ValueSome inner'

            match inner with
            | ValueSome x -> Future.Core.poll waker x
            | ValueNone -> Poll.Pending

    let delay (creator: unit -> Future<'a>) : Future<'a> =
        let mutable inner: Future<'a> voption = ValueNone
        Core.create ^fun waker ->
            match inner with
            | ValueSome fut -> fut.Poll(waker)
            | ValueNone ->
                let fut = creator ()
                inner <- ValueSome fut
                fut.Poll(waker)

    let ignore future =
        Core.create ^fun waker ->
            match Future.Core.poll waker future with
            | Poll.Ready _ -> Poll.Ready ()
            | Poll.Pending -> Poll.Pending
