namespace FSharp.Control.Futures.Lock

// TODO

[<RequireQualifiedAccess>]
[<Struct>]
type BarrierWaitResult =
    | Leader
    | Follower
