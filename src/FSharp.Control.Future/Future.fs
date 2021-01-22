namespace FSharp.Control.Future

open System
open System.Threading


[<Struct>]
type Poll<'a> =
    | Ready of 'a
    | Pending
    | Cancelled

module Poll =
    
    let ready x = Ready x
    
    let bind binding = function
        | Ready x -> binding x
        | Pending -> Pending
        | Cancelled -> Cancelled
    
    let map mapping = bind (mapping >> ready)
    
    let join poll = poll |> bind id

    let tryReady poll =
        match poll with
        | Ready x -> Some x
        | _ -> None


type Waker = unit -> unit


[<Struct>]
type Future<'a> = Future of (Waker -> Poll<'a>)

[<RequireQualifiedAccess>]
module Future = 
    
    let create f = Future f
    
    let poll waker (Future f) = f waker
    
    let single value : Future<'a> =
        create ^fun _ -> value |> Ready
    
    let bind (binding: 'a -> Future<'b>) (fa: Future<'a>) : Future<'b> =
        let mutable stateA = ValueSome fa
        let mutable (stateB: Future<'b> voption) = ValueNone
        create ^fun waker ->
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
    
    let merge (f1: Future<'a>) (f2: Future<'b>) : Future<'a * 'b> =
        let mutable r1 = None
        let mutable r2 = None
        create ^fun waker ->
            match poll waker f1 with
            | Ready x1 -> r1 <- Some x1
            | _ -> ()
            match poll waker f2 with
            | Ready x2 -> r2 <- Some x2
            | _ -> ()
            match r1, r2 with
            | Some x1, Some x2 -> Ready (x1, x2)
            | _ -> Pending
