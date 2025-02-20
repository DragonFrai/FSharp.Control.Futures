namespace FSharp.Control.Futures.Examples.Echo

open System
open System.Net
open System.Net.Sockets
open System.Text
open FSharp.Control.Futures
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.IO


module EchoReceiver =

    let parseArgs (argv: string list) =
        let target =
            match argv with
            | [ target ] -> target
            | _ -> failwith "Invalid args. Expected {target}. Example 'localhost:9090'"

        let hostname, port =
            match target.Split(":") with
            | [| hostname; port |] -> hostname, Int32.Parse(port)
            | _ -> failwith "Invalid target format. Expected {hostname}:{port}. Example 'localhost:9090'"

        hostname, port

    let serveClient (tcpClient: TcpClient) : Future<unit> = future {
        let tcpClientStream = tcpClient.GetStream()
        let buffer = Array.create 1024 0uy

        let rec serveLoop () = future {
            let! bytes = tcpClientStream.FutureRead(buffer, 0, buffer.Length)
            match bytes with
            | 0 -> return ()
            | bytes ->
                let message = Encoding.UTF8.GetString(buffer.AsSpan().Slice(0, bytes))
                printfn $"Received: '{message}'"

                let reply = message
                printfn $"Sending reply: '{reply}'"
                let replyBytes = Encoding.UTF8.GetBytes(reply)
                do! tcpClientStream.FutureWrite(replyBytes, 0, replyBytes.Length)

                return! serveLoop ()
        }
        try
            return! serveLoop ()
        with ex ->
            printfn $"Exception during service:\n{ex}"
    }

    let main (argv: string list) : int =
        let hostname, port = parseArgs argv

        let fut = future {
            let ipAddr = IPAddress.Parse(hostname)
            use tcpListener = new TcpListener(ipAddr, port)

            tcpListener.Start()
            let rec acceptLoop () = future {
                let! tcpClient = Future.ofTask (tcpListener.AcceptTcpClientAsync())
                printfn $"New client accepted"
                let _serveFutureTask = Runtime.spawn ThreadPoolRuntime.Instance (serveClient tcpClient)
                return! acceptLoop ()
            }
            return! acceptLoop ()
        }
        Future.runBlocking fut

        0
