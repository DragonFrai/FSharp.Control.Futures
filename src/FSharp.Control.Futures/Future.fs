namespace rec FSharp.Control.Futures

open System
open System.Threading


[<Struct>]
type Poll<'a> =
    | Ready of 'a
    | Pending

[<RequireQualifiedAccess>]
module Poll =
    let inline onReady (f: 'a -> unit) (x: Poll<'a>) : unit =
        match x with
        | Ready x -> f x
        | Pending -> ()

type Waker = unit -> unit

/// # Future poll schema
/// [ Pending -> ...(may be infinite)... -> Pending ] -> Ready x1 -> ... -> Ready xn
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

        let inline poll waker (fut: Future<'a>) = fut.Poll(waker)

        let getWaker = create Ready

        let unit = create ^fun _ -> Ready ()

        let never<'a> = create ^fun _ -> Poll<'a>.Pending


    let inline bindPoll' (f: 'a -> Poll<'b>) (x: Poll<'a>) : Poll<'b> =
        match x with
        | Ready x -> f x
        | Pending -> Pending

    let ready value = Core.create ^fun _ -> Ready value

    let unit () = Core.unit

    let lazy' (f: unit -> 'a) : Future<'a> =
        let mutable x = Unchecked.defaultof<'a>
        let mutable func = f
        Core.create ^fun _ ->
            if obj.ReferenceEquals(Unchecked.defaultof<_>, func)
            then Ready x
            else
                x <- func()
                func <- Unchecked.defaultof<_>
                Ready x

    let never () : Future<'a> = Core.never

    let bind (binder: 'a -> Future<'b>) (fut: Future<'a>) : Future<'b> =
        let mutable futA = fut
        let mutable futB = ValueNone
        Core.create ^fun waker ->
            match futB with
            | ValueNone ->
                match Future.Core.poll waker futA with
                     | Ready x ->
                         let futB' = binder x
                         futB <- ValueSome futB'
                         futA <- Unchecked.defaultof<_>
                         Future.Core.poll waker futB'
                     | Pending -> Pending
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
                    Ready r
            | ValueSome x -> Ready x

    let apply (f: Future<'a -> 'b>) (fut: Future<'a>) : Future<'b> =
        let mutable rf = ValueNone
        let mutable r1 = ValueNone
        Core.create ^fun waker ->
            Future.Core.poll waker f |> Poll.onReady ^fun f -> rf <- ValueSome f
            Future.Core.poll waker fut |> Poll.onReady ^fun x1 -> r1 <- ValueSome x1
            match rf, r1 with
            | ValueSome f, ValueSome x1 ->
                Ready (f x1)
            | _ -> Pending

    // TODO: rewrite to interlocked
    let merge (fut1: Future<'a>) (fut2: Future<'b>) : Future<'a * 'b> =
        let nullWaker: Waker = Unchecked.defaultof<_>

        let syncObj = obj()

        let mutable fut1 = fut1
        let mutable fut2 = fut2

        // when future is Pending and current waker = null, then waker already called of other branch
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
            currentWaker <- waker

            lock syncObj ^fun () ->
                if firstRequirePoll then
                    firstRequirePoll <- false
                    let x = fut1.Poll(proxyWaker1)
                    match x with
                    | Pending -> ()
                    | Ready x ->
                        r1 <- ValueSome x
                        fut1 <- Unchecked.defaultof<_>

                if secondRequirePoll then
                    secondRequirePoll <- false
                    let x = fut2.Poll(proxyWaker2)
                    match x with
                    | Pending -> ()
                    | Ready x ->
                        r2 <- ValueSome x
                        fut2 <- Unchecked.defaultof<_>

                match r1, r2 with
                | ValueSome x1, ValueSome x2 -> Ready (x1, x2)
                | _ -> Pending

    let join (fut: Future<Future<'a>>) : Future<'a> =
        let mutable inner = ValueNone
        Core.create ^fun waker ->
            if inner.IsNone then
                Future.Core.poll waker fut
                |> Poll.onReady ^fun inner' ->
                    inner <- ValueSome inner'

            match inner with
            | ValueSome x -> Future.Core.poll waker x
            | ValueNone -> Pending

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
            | Ready _ -> Ready ()
            | Pending -> Pending
