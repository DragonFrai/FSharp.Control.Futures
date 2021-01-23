namespace FSharp.Control.Futures

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

type CancellableFuture<'a> = CancellationToken -> Future<'a>

exception FutureCancelledException of string

[<RequireQualifiedAccess>]
module Future =

    let inline private bindPoll' (f: 'a -> Poll<'b>) (x: Poll<'a>) : Poll<'b> =
        match x with
        | Ready x -> f x
        | Pending -> Pending

    let poll waker (Future f) = f waker

    let ready value : Future<'a> =
        Future ^fun _ -> Ready value

    let lazy' (f: unit -> 'a) : Future<'a> =
        let mem = Lazy.Create f
        Future ^fun _ -> Ready mem.Value

    let never () : Future<'a> =
        Future ^fun _ -> Pending

    let bind (binder: 'a -> Future<'b>) (fut: Future<'a>) : Future<'b> =
        let mutable (fut2: Future<'b> voption) = ValueNone
        Future ^fun waker ->
            match fut2 with
            | ValueSome fut2 -> poll waker fut2
            | ValueNone ->
                poll waker fut
                |> bindPoll' ^fun x ->
                    let fut2' = binder x
                    fut2 <- ValueSome fut2'
                    poll waker fut2'

    let map (mapping: 'a -> 'b) (fut: Future<'a>) : Future<'b> =
        let mutable value = None
        Future ^fun waker ->
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
        Future ^fun waker ->
            poll waker f |> Poll.onReady ^fun f -> rf <- Some f
            poll waker fut |> Poll.onReady ^fun x1 -> r1 <- Some x1
            match rf, r1 with
            | Some f, Some x1 ->
                Ready (f x1)
            | _ -> Pending

    let merge (fut1: Future<'a>) (fut2: Future<'b>) : Future<'a * 'b> =
        let mutable r1 = None
        let mutable r2 = None
        Future ^fun waker ->
            poll waker fut1 |> Poll.onReady ^fun x1 -> r1 <- Some x1
            poll waker fut2 |> Poll.onReady ^fun x2 -> r2 <- Some x2
            match r1, r2 with
            | Some x1, Some x2 -> Ready (x1, x2)
            | _ -> Pending

    let join (fut: Future<Future<'a>>) : Future<'a> =
        let mutable inner = ValueNone
        Future ^fun waker ->
            if inner.IsNone then poll waker fut |> Poll.onReady ^fun inner' -> inner <- ValueSome inner'
            match inner with
            | ValueSome x -> poll waker x
            | ValueNone -> Pending

    let getWaker = Future Ready

    let ignore future = Future ^fun waker ->
        match poll waker future with
        | Ready _ -> Ready ()
        | Pending -> Pending
