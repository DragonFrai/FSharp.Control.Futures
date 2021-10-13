namespace FSharp.Control.Futures.IO

open System.IO

open FSharp.Control.Futures
open FSharp.Control.Futures.Transforms.FutureApmTransforms

[<RequireQualifiedAccess>]
module Future =

    module Stream =

        let read (buffer: byte array) (offset: int) (count: int) (stream: Stream) : Future<int> =
            let beginMethod ac s = stream.BeginRead(buffer, offset, count, ac, s)
            let endMethod ar = stream.EndRead(ar)
            Future.ofBeginEnd beginMethod endMethod

        let write (buffer: byte array) (offset: int) (count: int) (stream: Stream) : Future<unit> =
            let beginMethod ac s = stream.BeginWrite(buffer, offset, count, ac, s)
            let endMethod ar = stream.EndWrite(ar)
            Future.ofBeginEnd beginMethod endMethod

