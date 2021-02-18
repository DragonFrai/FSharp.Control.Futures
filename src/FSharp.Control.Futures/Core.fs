namespace FSharp.Control.Futures

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
