module FSharp.Control.Futures.Playground.SimpleRGrep

open FSharp.Control.Futures
open FSharp.Control.Futures.Examples.Grep
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.Sync


let findFilesRec (root: string) (files: Mailbox<string>) (filesCrawledEvent: Event) = future {
    do! GrepUtils.allFilesRec root (fun file -> future {
        match file with
        | None -> filesCrawledEvent.Set()
        | Some file -> files.Send(file)
    })
}

let scanAllRec (path: string) (pattern: string) (runtime: IRuntime) (parallelismLevel: int) = future {
    let files = Mailbox<string>()
    let filesCrawled = Event()
    let consoleMutex = Mutex()

    // [ Spawn files recursive enumeration workers ]
    let fileCrawlerTask = Runtime.spawn runtime (findFilesRec path files filesCrawled)

    // [ Spawn files pattern scanning workers ]
    let rec scanFileWorker () = future {
        let! file =
            Future.first
                (files.Receive() |> Future.map Some)
                (filesCrawled.Wait() |> Future.map (fun () -> None))
        match file with
        | None -> return ()
        | Some file ->
            do! GrepUtils.scanFile file pattern (fun (result: ScanResult) -> future {
                match result.Result with
                | Error _err ->
                    ()
                | Ok entry ->
                    do! consoleMutex.Lock()
                    printfn $"Entry at at {entry.Line + 1}, {entry.Column + 1} in '{file}':\n{entry.LineStr}\n"
                    do consoleMutex.Unlock()
            })
            return! scanFileWorker ()
    }
    let workers = seq {
        for _ in 1..parallelismLevel do
            yield Runtime.spawn runtime (scanFileWorker ())
    }

    // [ Await workers ]
    let awaitAndThrowOnError (fTask: IFutureTask<'a>) : Future<unit> = future {
        let! r = fTask.Await()
        match r with
        | Ok _ -> ()
        | Error err -> failwith $"{err}"
    }
    for worker in workers do
        do! awaitAndThrowOnError worker
    do! awaitAndThrowOnError fileCrawlerTask
}

module SimpleRipGrep =

    let parseArgs (argv: string list) =
        let pattern, rootPath =
            match argv with
            | [ target; message ] -> target, message
            | _ -> failwith "Invalid args. Expected {pattern} {rootPath}. Example 'hello' './txtFiles"
        pattern, rootPath

    let main (argv: string list) : int =
        let pattern, rootPath = parseArgs argv

        let runtime = ThreadPoolRuntime.Instance
        let parallelism = 10

        Future.runBlocking (future {
            do! scanAllRec rootPath pattern runtime parallelism
        })

        0
