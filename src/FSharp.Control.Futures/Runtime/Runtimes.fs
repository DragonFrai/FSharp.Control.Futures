namespace FSharp.Control.Futures.Runtime

open FSharp.Control.Futures.Runtime.ThreadPoolRuntime

[<AutoOpen>]
module ThreadPoolRuntimePrelude =
    [<RequireQualifiedAccess>]
    module ThreadPoolRuntime =
        let instance = ThreadPoolRuntime.Instance
        let inline spawn fut = Runtime.spawn ThreadPoolRuntime.Instance fut
