namespace FSharp.Control.Futures.Sync

// TODO

[<RequireQualifiedAccess>]
[<Struct>]
type BarrierWaitResult =
    | Leader
    | Follower
