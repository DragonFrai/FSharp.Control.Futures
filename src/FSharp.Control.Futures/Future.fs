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

[<Struct; NoEquality; NoComparison>]
type Future<'a> = Future of (Waker -> Poll<'a>)

type CancellableFuture<'a> = CancellationToken -> Future<'a>

exception FutureCancelledException of string

[<RequireQualifiedAccess>]
module Future =

    let inline bindPoll' (f: 'a -> Poll<'b>) (x: Poll<'a>) : Poll<'b> =
        match x with
        | Ready x -> f x
        | Pending -> Pending

    let inline create (f: Waker -> Poll<'a>) = Future f

    let inline poll waker (Future fut) = fut waker

    let ready value = create ^fun _ -> Ready value

    let lazy' (f: unit -> 'a) : Future<'a> =
        let mem = Lazy.Create f
        create ^fun _ -> Ready mem.Value

    let never () : Future<'a> =
        create ^fun _ -> Pending

    let bind binder fut =
        let futA = fut
        let binder = binder
        let mutable futB = ValueNone
        create ^fun waker ->
            match futB with
            | ValueNone ->
                match Future.poll waker futA with
                     | Ready x ->
                         let futB' = binder x
                         futB <- ValueSome futB'
                         Future.poll waker futB'
                     | Pending -> Pending
            | ValueSome futB -> Future.poll waker futB

    let map (mapping: 'a -> 'b) (fut: Future<'a>) : Future<'b> =
        let mutable value = None
        create ^fun waker ->
            match value with
            | None ->
                poll waker fut
                |> bindPoll' ^fun x ->
                    let r = mapping x
                    value <- Some r
                    Ready r
            | Some x -> Ready x

    let apply (f: Future<'a -> 'b>) (fut: Future<'a>) : Future<'b> =
        let mutable rf = None
        let mutable r1 = None
        create ^fun waker ->
            poll waker f |> Poll.onReady ^fun f -> rf <- Some f
            poll waker fut |> Poll.onReady ^fun x1 -> r1 <- Some x1
            match rf, r1 with
            | Some f, Some x1 ->
                Ready (f x1)
            | _ -> Pending

    let merge (fut1: Future<'a>) (fut2: Future<'b>) : Future<'a * 'b> =
        let mutable r1 = ValueNone
        let mutable r2 = ValueNone
        create ^fun waker ->
            poll waker fut1 |> Poll.onReady ^fun x1 -> r1 <- ValueSome x1
            poll waker fut2 |> Poll.onReady ^fun x2 -> r2 <- ValueSome x2
            match r1, r2 with
            | ValueSome x1, ValueSome x2 -> Ready (x1, x2)
            | _ -> Pending

    let join (fut: Future<Future<'a>>) : Future<'a> =
        let mutable inner = ValueNone
        create ^fun waker ->
            if inner.IsNone then poll waker fut |> Poll.onReady ^fun inner' -> inner <- ValueSome inner'
            match inner with
            | ValueSome x -> poll waker x
            | ValueNone -> Pending

    let getWaker = create Ready

    let ignore future = create ^fun waker ->
        match poll waker future with
        | Ready _ -> Ready ()
        | Pending -> Pending

