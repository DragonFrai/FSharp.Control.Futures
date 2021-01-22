[<AutoOpen>]
module FSharp.Control.Future.Core


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

module Optimized =

    [<RequireQualifiedAccess>]
    module Future =



[<RequireQualifiedAccess>]
module Future =

    let create f = Future f

    let poll waker (Future f) = f waker

    let ready value : Future<'a> =
        create ^fun _ -> value |> Ready




