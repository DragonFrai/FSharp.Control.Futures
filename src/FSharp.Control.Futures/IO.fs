namespace FSharp.Control.Futures.IO

open System.IO

open FSharp.Control.Futures
open FSharp.Control.Futures.Transforms.FutureApmTransforms

[<AutoOpen>]
module StreamExtensions =

    type Stream with

        member this.FutureRead(buffer: byte array, offset: int, count: int): Future<int> =
            let beginMethod ac s = this.BeginRead(buffer, offset, count, ac, s)
            let endMethod ar = this.EndRead(ar)
            Future.ofBeginEnd beginMethod endMethod

        member this.FutureWrite(buffer: byte array, offset: int, count: int): Future<unit> =
            let beginMethod ac s = this.BeginWrite(buffer, offset, count, ac, s)
            let endMethod ar = this.EndWrite(ar)
            Future.ofBeginEnd beginMethod endMethod

        member this.FutureReadByte(): Future<int> =
            let buffer = [| 0uy |]
            this.FutureRead(buffer, 0, 1)
            |> Future.map (fun c -> if c <> 0 then int buffer[0] else -1)

        member this.FutureWriteByte(value: byte): Future<unit> =
            this.FutureWrite([| value |], 0, 1)


[<RequireQualifiedAccess>]
module Future =

    module Stream =

        let inline read (buffer: byte array) (offset: int) (count: int) (stream: Stream) : Future<int> =
            stream.FutureRead(buffer, offset, count)

        let inline write (buffer: byte array) (offset: int) (count: int) (stream: Stream) : Future<unit> =
            stream.FutureWrite(buffer, offset, count)

        let inline readByte (stream: Stream) : Future<int> =
            stream.FutureReadByte()

        let inline writeByte (value: byte) (stream: Stream) : Future<unit> =
            stream.FutureWriteByte(value)
