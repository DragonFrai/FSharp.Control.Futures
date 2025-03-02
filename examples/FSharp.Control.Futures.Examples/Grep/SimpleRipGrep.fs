module FSharp.Control.Futures.Playground.SimpleRGrep

open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open FSharp.Control.Futures
open FSharp.Control.Futures.Examples.Grep
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.Sync




let findFilesRec (root: string) (files: MutexVar<Queue<string>>) (isEnded: bool ref) = future {
    let onFind file = future {
        match file with
        | None -> isEnded.Value <- true
        | Some file ->
            do! files.MutateSync(fun files -> files.Enqueue(file))
    }
    do! GrepUtils.allFilesRec root onFind
}

let scanAllRec (path: string) (pattern: string) (runtime: IRuntime) (parallelismLevel: int) = future {
    let files = MutexVar(Queue())
    let isEnded = ref false
    // let entries = MutexCell(Queue())
    let consoleMutex = Mutex()
    let fileCrawler = Runtime.spawn runtime (findFilesRec path files isEnded)

    let rec scanFileWorker () = future {
        match! files.LockSync(_.TryDequeue()) with
        | true, file ->
            let onFind (entry: Entry) = future {
                match entry.Result with
                | Error _err ->
                    // do! consoleMutex.Lock()
                    // printfn $"Error in '{file}': {err}"
                    // do consoleMutex.Unlock()
                    ()
                | Ok res ->
                    do! consoleMutex.Lock()
                    printfn $"Entry at at {res.Line + 1}, {res.Column + 1} in '{file}':\n{res.LineStr}\n"
                    do consoleMutex.Unlock()
            }
            do! GrepUtils.scanFile pattern onFind file
            return! scanFileWorker ()
        | false, _ ->
            if isEnded.Value
            then return ()
            else
                do! Future.yieldWorkflow ()
                return! scanFileWorker ()
    }

    let workers = seq {
        for _ in 1..parallelismLevel do
            yield Runtime.spawn runtime (scanFileWorker ())
    }

    for worker in workers do
        do! worker.Await() |> Future.ignore
    do! fileCrawler.Await() |> Future.ignore

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
