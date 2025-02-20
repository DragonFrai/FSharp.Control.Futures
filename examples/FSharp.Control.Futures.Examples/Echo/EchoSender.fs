namespace FSharp.Control.Futures.Examples.Echo

open System
open System.Net.Sockets
open System.Text
open FSharp.Control.Futures
open FSharp.Control.Futures.IO


module EchoSender =

    let parseArgs (argv: string list) =
        let target, message =
            match argv with
            | [ target; message ] -> target, message
            | _ -> failwith "Invalid args. Expected {target} {message}. Example 'localhost:9090' 'hello!'"

        let hostname, port =
            match target.Split(":") with
            | [| hostname; port |] -> hostname, Int32.Parse(port)
            | _ -> failwith "Invalid target format. Expected {hostname}:{port}. Example 'localhost:9090'"

        hostname, port, message

    let main (argv: string list) : int =
        let hostname, port, message = parseArgs argv

        let fut = future {
            use tcpClient = new TcpClient()
            do! Future.ofUnitTask (tcpClient.ConnectAsync(hostname, port))
            let stream = tcpClient.GetStream()

            printfn $"Sending: {message}"
            let messageBytes = Encoding.UTF8.GetBytes(message)
            do! stream.FutureWrite(messageBytes, 0, messageBytes.Length)

            printfn $"Waiting reply..."
            let replyBuffer = Array.create 1024 0uy
            let! replyLength = stream.FutureRead(replyBuffer, 0, replyBuffer.Length)
            let replyMessage = Encoding.UTF8.GetString(replyBuffer.AsSpan().Slice(0, replyLength))
            printfn $"Reply: {replyMessage}"

            ()
        }
        Future.runBlocking fut

        0
