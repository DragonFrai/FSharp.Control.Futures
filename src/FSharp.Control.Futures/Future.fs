namespace rec FSharp.Control.Futures

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

[<Struct>]
type Future<'a> = Future of (Waker -> Poll<'a>)

[<RequireQualifiedAccess>]
module FutureCore =

    let inline create (f: Waker -> Poll<'a>) = Future f

    let inline poll waker (Future fut) = fut waker


[<RequireQualifiedAccess>]
module Future =

    let inline bindPoll' (f: 'a -> Poll<'b>) (x: Poll<'a>) : Poll<'b> =
        match x with
        | Ready x -> f x
        | Pending -> Pending

    let ready value = FutureCore.create ^fun _ -> Ready value

    let lazy' (f: unit -> 'a) : Future<'a> =
        let mem = Lazy.Create f
        FutureCore.create ^fun _ -> Ready mem.Value

    let never () : Future<'a> =
        FutureCore.create ^fun _ -> Pending

    let bind binder fut =
        let futA = fut
        let binder = binder
        let mutable futB = ValueNone
        FutureCore.create ^fun waker ->
            match futB with
            | ValueNone ->
                match FutureCore.poll waker futA with
                     | Ready x ->
                         let futB' = binder x
                         futB <- ValueSome futB'
                         FutureCore.poll waker futB'
                     | Pending -> Pending
            | ValueSome futB -> FutureCore.poll waker futB

    let map (mapping: 'a -> 'b) (fut: Future<'a>) : Future<'b> =
        let mutable value = None
        FutureCore.create ^fun waker ->
            match value with
            | None ->
                FutureCore.poll waker fut
                |> bindPoll' ^fun x ->
                    let r = mapping x
                    value <- Some r
                    Ready r
            | Some x -> Ready x

    let apply (f: Future<'a -> 'b>) (fut: Future<'a>) : Future<'b> =
        let mutable rf = None
        let mutable r1 = None
        FutureCore.create ^fun waker ->
            FutureCore.poll waker f |> Poll.onReady ^fun f -> rf <- Some f
            FutureCore.poll waker fut |> Poll.onReady ^fun x1 -> r1 <- Some x1
            match rf, r1 with
            | Some f, Some x1 ->
                Ready (f x1)
            | _ -> Pending

    let merge (fut1: Future<'a>) (fut2: Future<'b>) : Future<'a * 'b> =
        let mutable r1 = ValueNone
        let mutable r2 = ValueNone
        FutureCore.create ^fun waker ->
            FutureCore.poll waker fut1 |> Poll.onReady ^fun x1 -> r1 <- ValueSome x1
            FutureCore.poll waker fut2 |> Poll.onReady ^fun x2 -> r2 <- ValueSome x2
            match r1, r2 with
            | ValueSome x1, ValueSome x2 -> Ready (x1, x2)
            | _ -> Pending

    let join (fut: Future<Future<'a>>) : Future<'a> =
        let mutable inner = ValueNone
        FutureCore.create ^fun waker ->
            if inner.IsNone then FutureCore.poll waker fut |> Poll.onReady ^fun inner' -> inner <- ValueSome inner'
            match inner with
            | ValueSome x -> FutureCore.poll waker x
            | ValueNone -> Pending

    let getWaker = FutureCore.create Ready

    let ignore future = FutureCore.create ^fun waker ->
        match FutureCore.poll waker future with
        | Ready _ -> Ready ()
        | Pending -> Pending

