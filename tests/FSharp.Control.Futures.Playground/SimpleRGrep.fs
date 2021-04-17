module FSharp.Control.Futures.Playground.SimpleRGrep

open System.Collections.Concurrent
open System.IO
open FSharp.Control.Futures
open FSharp.Control.Futures.Scheduling
open FSharp.Control.Futures.Streams


type Entry =
    { FilePath: string
      SymbolIdx: int }

let readFile (path: string) = future {
    let mutable reader = null
    do
        try
            reader <- new StreamReader(path)
        with
        | e -> ()

    if reader <> null then
        return! reader.ReadToEndAsync() |> Future.ofTask |> Future.map (fun s -> reader.Dispose(); s)
    else
        return! Future.ready ""
}


let findFilesRec (root: string) (files: ConcurrentQueue<string>) (isEnded: bool ref) = future {
    let rec scanDir dir = future {
        for file in Directory.GetFiles(dir) do
            files.Enqueue(file)
        for dir in Directory.GetDirectories(dir) do
            do! scanDir dir
        do! Future.yieldWorkflow ()
    }
    do! scanDir root
    isEnded.Value <- true
}

let scanAllRec (path: string) (content: string) (scheduler: IScheduler) (parallelismLevel: int) = future {
    let files = ConcurrentQueue()
    let isEnded = false
    let fileCrawler = Scheduler.spawnOn scheduler (findFilesRec path files (ref isEnded))

    let scanString (s: string) (fileSource: string) =
        let mutable current = 0
        let mutable entryIdx = s.IndexOf(content, current)
        let rec loop () =
            if entryIdx = -1 then Stream.empty
            else
                stream {
                    current <- entryIdx + content.Length
                    yield { FilePath = fileSource; SymbolIdx = entryIdx }
                    entryIdx <- s.IndexOf(content, current)
                    yield! loop ()
                }
        loop ()

    let scanFileWorker () = future {
        while (not isEnded) do
            match files.TryDequeue() with
            | true, file ->
                let! text = readFile file
                for entry in scanString text file do
                    printfn "in file\n\t%A\n\tat character %A" entry.FilePath entry.SymbolIdx
            | false, _ -> ()
            do! Future.yieldWorkflow ()
    }

    let workers = seq {
        for _ in 1..parallelismLevel do
            yield Scheduler.spawnOn scheduler (scanFileWorker ())
    }

    // Технически здесь это не обязательно
    for worker in workers do
        do! worker
    do! fileCrawler
}

