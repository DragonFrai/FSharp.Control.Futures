module FSharp.Control.Futures.Playground.SimpleRGrep

open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open FSharp.Control.Futures
open FSharp.Control.Futures.Runtime
open FSharp.Control.Futures.Sync


type EntryResult =
    { SymbolIdx: int }

type Entry =
    { FilePath: string
      Result: Result<EntryResult, string> }

let [<Literal>] SizeLimit = 1024L*1024L*8L

let readFileLimited (sizeLimit: int64) (path: string) = future {
    let info = FileInfo(path)
    if info.Length > sizeLimit then
        return Error "File is too big"
    else
        use reader = new StreamReader(path)
        let! data = reader.ReadToEndAsync() |> Future.ofTask
        return Ok data
}


let findFilesRec (root: string) (files: MutexVar<Queue<string>>) (isEnded: bool ref) = future {
    let rec scanDir dir = future {
        for file in Directory.GetFiles(dir) do
            do! files.Mutate(_.Enqueue(file))
        for dir in Directory.GetDirectories(dir) do
            do! scanDir dir
        do! Future.yieldWorkflow ()
    }
    do! scanDir root
    isEnded.Value <- true
}

let scanAllRec (path: string) (content: string) (runtime: IRuntime) (parallelismLevel: int) = future {
    let files = MutexVar(Queue())
    let isEnded = ref false
    // let entries = MutexCell(Queue())
    let consoleMutex = Mutex()
    let fileCrawler = Runtime.spawn runtime (findFilesRec path files isEnded)

    let scanString (s: string) (fileSource: string) (*(_entries: MutexCell<Queue<Entry>>)*) = future {
        let mutable entryIdx = s.IndexOf(content, 0)
        while entryIdx <> -1 do
            let current = entryIdx + content.Length
            //let entry = { FilePath = fileSource; Result = Ok { SymbolIdx = entryIdx } }

            do! consoleMutex.Lock()
            printfn $"Entry in '{fileSource}': {entryIdx}"
            do consoleMutex.Unlock()

            // do! entries.Read(_.Enqueue(entry))
            entryIdx <- s.IndexOf(content, current)
            do! Future.yieldWorkflow ()
    }

    let rec scanFileWorker () = future {
        match! files.Lock(_.TryDequeue()) with
        | true, file ->
            // do! consoleMutex.Lock()
            // printfn $"Scanning file: {file}"
            // do consoleMutex.Unlock()

            let! text = readFileLimited SizeLimit file
            match text with
            | Error e ->
                do! consoleMutex.Lock()
                printfn $"Error in '{file}': {e}"
                do consoleMutex.Unlock()
                // do! entries.Read(_.Enqueue(entry))
                return! scanFileWorker ()
            | Ok text ->
                do! scanString text file (*entries*)
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

    // Технически здесь это не обязательно
    for worker in workers do
        do! worker.Await() |> Future.ignore
    do! fileCrawler.Await() |> Future.ignore

    // entries.Mutate(fun q ->
    //     while q.Count <> 0 do
    //         let e = q.Dequeue()
    //         printfn $"{e.FilePath}: {e.Result}"
    // )
}

