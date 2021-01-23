namespace FSharp.Control.Future

open System.Threading


[<Struct>]
type Poll<'a> =
    | Ready of 'a
    | Pending

module Poll =

    let ready x = Ready x

    let bind binding = function
        | Ready x -> binding x
        | Pending -> Pending

    let map mapping = bind (mapping >> ready)

    let join poll = poll |> bind id

    let tryReady poll =
        match poll with
        | Ready x -> Some x
        | _ -> None


type Waker = unit -> unit


[<Struct>]
type Future<'a> = Future of (Waker -> Poll<'a>)

type CancellableFuture<'a> = CancellationToken -> Future<'a>

exception FutureCancelledException of string

[<RequireQualifiedAccess>]
module Future =

    let poll waker (Future f) = f waker

    let ready value : Future<'a> =
        Future ^fun _ -> Ready value

    let lazy' (f: unit -> 'a) : Future<'a> =
        let mem = Lazy.Create f
        Future ^fun _ -> Ready mem.Value

    let never () : Future<'a> =
        Future ^fun _ -> Pending

    let bind (binder: 'a -> Future<'b>) (fut: Future<'a>) : Future<'b> =
        let mutable stateA = ValueSome fut
        let mutable (stateB: Future<'b> voption) = ValueNone
        Future ^fun waker ->
            match stateB with
            | ValueSome fb -> poll waker fb
            | ValueNone ->
                match stateA with
                | ValueSome fa ->
                    match poll waker fa with
                    | Ready x ->
                        let fb = binder x
                        stateB <- ValueSome fb
                        stateA <- ValueNone
                        poll waker fb
                    | Pending -> Pending
                | ValueNone -> invalidOp "Unreachable"

    let map (mapping: 'a -> 'b) (fut: Future<'a>) : Future<'b> =
        let mutable value = None
        Future ^fun waker ->
            match value with
            | None ->
                match poll waker fut with
                | Ready x ->
                    let r = mapping x
                    value <- Some r
                    Ready r
                | Pending -> Pending
            | Some x -> Ready x

    let apply (f: Future<'a -> 'b>) (fut: Future<'a>) : Future<'b> =
        let mutable rf = None
        let mutable r1 = None
        Future ^fun waker ->
            match poll waker f with
            | Ready f -> rf <- Some f
            | _ -> ()
            match poll waker fut with
            | Ready x1 -> r1 <- Some x1
            | _ -> ()
            match rf, r1 with
            | Some f, Some x1 ->
                Ready (f x1)
            | _ -> Pending

    let merge (fut1: Future<'a>) (fut2: Future<'b>) : Future<'a * 'b> =
        let mutable r1 = None
        let mutable r2 = None
        Future ^fun waker ->
            match poll waker fut1 with
            | Ready x1 -> r1 <- Some x1
            | _ -> ()
            match poll waker fut2 with
            | Ready x2 -> r2 <- Some x2
            | _ -> ()
            match r1, r2 with
            | Some x1, Some x2 -> Ready (x1, x2)
            | _ -> Pending

    let join (fut: Future<Future<'a>>) : Future<'a> =
        let mutable inner = ValueNone
        Future ^fun waker ->
            if inner.IsNone then
                match poll waker fut with
                | Ready inner' ->
                    inner <- ValueSome inner'
                    Pending
                | Pending -> Pending
            else
            match inner with
            | ValueSome x -> poll waker x
            | ValueNone -> Pending

    let getWaker = Future Ready

    let ignore future = Future ^fun waker -> (poll waker future) |> Poll.map ignore
