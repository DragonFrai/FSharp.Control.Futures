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

type IFuture<'a> =
    abstract member Poll: Waker -> Poll<'a>

type CancellableFuture<'a> = CancellationToken -> IFuture<'a>

exception FutureCancelledException of string

[<NoComparison; NoEquality>]
type FuncFuture<'a>(poller: Waker -> Poll<'a>) =
    class
    end
    with interface IFuture<'a> with member _.Poll(waker) = poller waker

[<RequireQualifiedAccess>]
module Future =

    let inline private bindPoll' (f: 'a -> Poll<'b>) (x: Poll<'a>) : Poll<'b> =
        match x with
        | Ready x -> f x
        | Pending -> Pending

    let create (f: Waker -> Poll<'a>) = FuncFuture f :> IFuture<'a>

    let inline poll waker (fut: IFuture<_>) = fut.Poll(waker)

    let ready value : IFuture<'a> =
        create ^fun _ -> Ready value

    let lazy' (f: unit -> 'a) : IFuture<'a> =
        let mem = Lazy.Create f
        create ^fun _ -> Ready mem.Value

    let never () : IFuture<'a> =
        create ^fun _ -> Pending

    let bind (binder: 'a -> IFuture<'b>) (fut: IFuture<'a>) : IFuture<'b> =
        let mutable (fut2: IFuture<'b> voption) = ValueNone
        create ^fun waker ->
            match fut2 with
            | ValueSome fut2 -> poll waker fut2
            | ValueNone ->
                poll waker fut
                |> bindPoll' ^fun x ->
                    let fut2' = binder x
                    fut2 <- ValueSome fut2'
                    poll waker fut2'

    let map (mapping: 'a -> 'b) (fut: IFuture<'a>) : IFuture<'b> =
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

    let apply (f: IFuture<'a -> 'b>) (fut: IFuture<'a>) : IFuture<'b> =
        let mutable rf = None
        let mutable r1 = None
        create ^fun waker ->
            poll waker f |> Poll.onReady ^fun f -> rf <- Some f
            poll waker fut |> Poll.onReady ^fun x1 -> r1 <- Some x1
            match rf, r1 with
            | Some f, Some x1 ->
                Ready (f x1)
            | _ -> Pending

    let merge (fut1: IFuture<'a>) (fut2: IFuture<'b>) : IFuture<'a * 'b> =
        let mutable r1 = None
        let mutable r2 = None
        create ^fun waker ->
            poll waker fut1 |> Poll.onReady ^fun x1 -> r1 <- Some x1
            poll waker fut2 |> Poll.onReady ^fun x2 -> r2 <- Some x2
            match r1, r2 with
            | Some x1, Some x2 -> Ready (x1, x2)
            | _ -> Pending

    let join (fut: IFuture<IFuture<'a>>) : IFuture<'a> =
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
