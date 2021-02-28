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

[<AbstractClass>]
type Context() =
    abstract Wake: unit -> unit

/// # Future poll schema
/// [ Poll.Pending -> ...(may be infinite)... -> Poll.Pending ] -> Poll.Ready x1 -> ... -> Poll.Ready xn
///  x1 == x2 == ... == xn
[<Interface>]
type IFuture<'a> =
    abstract Poll: Context -> Poll<'a>

// I know, I know
type Future<'a> = IFuture<'a>

[<RequireQualifiedAccess>]
module Future =

    [<RequireQualifiedAccess>]
    module Core =

        let inline create (__expand_poll: Context -> Poll<'a>): Future<'a> =
            { new Future<'a> with member this.Poll(context) = __expand_poll context }

        let inline memoizeReady (poll: Context -> Poll<'a>) : Future<'a> =
            let mutable poll = poll // poll = null, when memoized
            let mutable result: 'a = Unchecked.defaultof<_>
            Core.create ^fun context ->
                if obj.ReferenceEquals(poll, null) then
                    Poll.Ready result
                else
                    let p = poll context
                    match p with
                    | Poll.Pending -> Poll.Pending
                    | Poll.Ready x ->
                        result <- x
                        poll <- Unchecked.defaultof<_>
                        Poll.Ready x

        let inline poll context (fut: Future<'a>) = fut.Poll(context)

        let getWaker = create Poll.Ready


    let inline bindPoll' (f: 'a -> Poll<'b>) (x: Poll<'a>) : Poll<'b> =
        match x with
        | Poll.Ready x -> f x
        | Poll.Pending -> Poll.Pending

    let ready value = Core.create ^fun _ -> Poll.Ready value

    let unit () = Core.create ^fun _ -> Poll.Ready ()

    let lazy' (f: unit -> 'a) : Future<'a> =
        Core.memoizeReady ^fun _ -> Poll.Ready (f ())

    let never () : Future<'a> = Core.create ^fun _ -> Poll<'a>.Pending

    let bind (binder: 'a -> Future<'b>) (fut: Future<'a>) : Future<'b> =
        // let binder = binder
        let mutable futA = fut
        let mutable futB = nullObj

        Core.create ^fun context ->
            if isNull futB then
                match Future.Core.poll context futA with
                | Poll.Ready x ->
                    futB <- binder x
                    // binder <- nullObj
                    futA <- nullObj
                    Future.Core.poll context futB
                | Poll.Pending -> Poll.Pending
            else
                Future.Core.poll context futB

    let map (mapping: 'a -> 'b) (fut: Future<'a>) : Future<'b> =
        let mutable _fut = fut // _fut = null, when memoized
        let mutable _mapping = mapping // _mapping = null, when memoized
        let mutable _value = Unchecked.defaultof<_>
        Core.create ^fun context ->
            if obj.ReferenceEquals(_mapping, null) then
                Poll.Ready _value
            else
                let p = _fut.Poll(context)
                match p with
                | Poll.Pending -> Poll.Pending
                | Poll.Ready x ->
                    let r = mapping x
                    _value <- r
                    _mapping <- Unchecked.defaultof<_>
                    _fut <- Unchecked.defaultof<_>
                    Poll.Ready r

    let apply (f: Future<'a -> 'b>) (fut: Future<'a>) : Future<'b> =
        let mutable rf = ValueNone
        let mutable r1 = ValueNone
        Core.create ^fun context ->
            Future.Core.poll context f |> Poll.onReady ^fun f -> rf <- ValueSome f
            Future.Core.poll context fut |> Poll.onReady ^fun x1 -> r1 <- ValueSome x1
            match rf, r1 with
            | ValueSome f, ValueSome x1 ->
                Poll.Ready (f x1)
            | _ -> Poll.Pending

    let merge (fut1: Future<'a>) (fut2: Future<'b>) : Future<'a * 'b> =

        let mutable _fut1 = fut1
        let mutable _fut2 = fut2

        // when future is Poll.Pending and current context = null, then context already called of other branch
        let mutable _r1 = ValueNone
        let mutable _r2 = ValueNone

        Core.create ^fun context ->
            if _r1.IsNone then
                match Core.poll context _fut1 with
                | Poll.Ready x ->
                    _r1 <- ValueSome x
                    _fut1 <- nullObj
                | Poll.Pending -> ()
            if _r2.IsNone then
                match Core.poll context _fut2 with
                | Poll.Ready x ->
                    _r2 <- ValueSome x
                    _fut2 <- nullObj
                | Poll.Pending -> ()

            match _r1, _r2 with
            | ValueSome x1, ValueSome x2 -> Poll.Ready (x1, x2)
            | _ -> Poll.Pending



    let join (fut: Future<Future<'a>>) : Future<'a> =
        let mutable inner = ValueNone
        Core.create ^fun context ->
            if inner.IsNone then
                Future.Core.poll context fut
                |> Poll.onReady ^fun inner' ->
                    inner <- ValueSome inner'

            match inner with
            | ValueSome x -> Future.Core.poll context x
            | ValueNone -> Poll.Pending

    let delay (creator: unit -> Future<'a>) : Future<'a> =
        let mutable inner: Future<'a> voption = ValueNone
        Core.create ^fun context ->
            match inner with
            | ValueSome fut -> fut.Poll(context)
            | ValueNone ->
                let fut = creator ()
                inner <- ValueSome fut
                fut.Poll(context)

    let ignore future =
        Core.create ^fun context ->
            match Future.Core.poll context future with
            | Poll.Ready _ -> Poll.Ready ()
            | Poll.Pending -> Poll.Pending
