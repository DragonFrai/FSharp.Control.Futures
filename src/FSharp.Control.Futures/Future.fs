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

[<AbstractClass>]
type Future<'a>() =
    abstract member Poll: Waker -> Poll<'a>

[<RequireQualifiedAccess>]
module Future =

    module Core =

        [<Obsolete("Inherit class from FSharp.Control.Futures.Future")>]
        let inline create (f: Waker -> Poll<'a>): Future<'a> =
            { new Future<'a>() with member this.Poll(waker) = f waker }

        let inline poll waker (fut: Future<'a>) = fut.Poll(waker)


    let inline bindPoll' (f: 'a -> Poll<'b>) (x: Poll<'a>) : Poll<'b> =
        match x with
        | Ready x -> f x
        | Pending -> Pending

    let ready value = Future.Core.create ^fun _ -> Ready value

    let lazy' (f: unit -> 'a) : Future<'a> =
        let mem = Lazy<'a>.Create(f)
        Future.Core.create ^fun _ -> Ready mem.Value

    let never () : Future<'a> =
        Future.Core.create ^fun _ -> Pending

    let bind (binder: 'a -> Future<'b>) (fut: Future<'a>) : Future<'b> =
        let futA = fut
        let binder = binder
        let mutable futB = ValueNone
        Future.Core.create ^fun waker ->
            match futB with
            | ValueNone ->
                match Future.Core.poll waker futA with
                     | Ready x ->
                         let futB' = binder x
                         futB <- ValueSome futB'
                         Future.Core.poll waker futB'
                     | Pending -> Pending
            | ValueSome futB -> Future.Core.poll waker futB

    let map (mapping: 'a -> 'b) (fut: Future<'a>) : Future<'b> =
        let mutable value = None
        Future.Core.create ^fun waker ->
            match value with
            | None ->
                Future.Core.poll waker fut
                |> bindPoll' ^fun x ->
                    let r = mapping x
                    value <- Some r
                    Ready r
            | Some x -> Ready x

    let apply (f: Future<'a -> 'b>) (fut: Future<'a>) : Future<'b> =
        let mutable rf = None
        let mutable r1 = None
        Future.Core.create ^fun waker ->
            Future.Core.poll waker f |> Poll.onReady ^fun f -> rf <- Some f
            Future.Core.poll waker fut |> Poll.onReady ^fun x1 -> r1 <- Some x1
            match rf, r1 with
            | Some f, Some x1 ->
                Ready (f x1)
            | _ -> Pending

    let merge (fut1: Future<'a>) (fut2: Future<'b>) : Future<'a * 'b> =
        let mutable r1 = ValueNone
        let mutable r2 = ValueNone
        Future.Core.create ^fun waker ->
            Future.Core.poll waker fut1 |> Poll.onReady ^fun x1 -> r1 <- ValueSome x1
            Future.Core.poll waker fut2 |> Poll.onReady ^fun x2 -> r2 <- ValueSome x2
            match r1, r2 with
            | ValueSome x1, ValueSome x2 -> Ready (x1, x2)
            | _ -> Pending

    let join (fut: Future<Future<'a>>) : Future<'a> =
        let mutable inner = ValueNone
        Future.Core.create ^fun waker ->
            if inner.IsNone then Future.Core.poll waker fut |> Poll.onReady ^fun inner' -> inner <- ValueSome inner'
            match inner with
            | ValueSome x -> Future.Core.poll waker x
            | ValueNone -> Pending

    let getWaker = Future.Core.create Ready

    let ignore future = Future.Core.create ^fun waker ->
        match Future.Core.poll waker future with
        | Ready _ -> Ready ()
        | Pending -> Pending

