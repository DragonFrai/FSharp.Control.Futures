module FSharp.Control.Future.Optimized

[<RequireQualifiedAccess>]
module Future =

    let poll waker (Future f) = f waker

    let ready value : Future<'a> =
        Future ^fun _ -> Ready value

    let lazy' (f: unit -> 'a) : Future<'a> =
        let mem = Lazy.Create f
        Future ^fun _ -> Ready mem.Value

    let bind (binding: 'a -> Future<'b>) (fut: Future<'a>) : Future<'b> =
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
                        let fb = binding x
                        stateB <- ValueSome fb
                        stateA <- ValueNone
                        poll waker fb
                    | Pending -> Pending
                    | Cancelled -> Cancelled
                | ValueNone -> invalidOp "Unreachable"

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
                | Cancelled -> Cancelled
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
