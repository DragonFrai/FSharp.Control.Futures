namespace FSharp.Control.Futures.Actors.Address


type SendError =
    | Full
    | Stopped
    | Timeout


// [<RequireQualifiedAccess>]
// type SendError =
//     | Closed
//     // | Timeout ???
//
// [<RequireQualifiedAccess>]
// type TryPushError =
//     | Full
//     | Closed
//
// [<RequireQualifiedAccess>]
// type PushError =
//     | Closed

