namespace FSharp.Control.Futures.Examples.Grep

open System.IO
open FSharp.Control.Futures


type EntryResult =
    { Line: int; Column: int; LineStr: string }

type Entry =
    { FilePath: string
      Result: Result<EntryResult, string> }

[<RequireQualifiedAccess>]
module GrepUtils =

    let allFilesRec (root: string) (onFind: string option -> Future<unit>) = future {
        let rec scanDir (path: string) = future {
            let attrs = File.GetAttributes(path)
            if not (attrs.HasFlag(FileAttributes.Directory)) then
                do! onFind (Some path)
            else
                for file in Directory.GetFiles(path) do
                    do! onFind (Some file)
                for dir in Directory.GetDirectories(path) do
                    do! scanDir dir
                do! Future.yieldWorkflow ()
        }
        do! scanDir root
        do! onFind None
    }

    let scanString (pattern: string) (onFind: int -> Future<unit>) (str: string) = future {
        let rec loop (currentIdx: int) (pattern: string) (str: string) = future {
            let entryIdx = str.IndexOf(pattern, currentIdx)
            if entryIdx = -1 then ()
            else
                do! onFind entryIdx
                do! Future.yieldWorkflow ()
                return! loop (entryIdx + 1) pattern str
        }
        return! loop 0 pattern str
    }

    let scanFile (pattern: string) (onFind: Entry -> Future<unit>) (path: string) = future {
        let sizeLimit = 1024 * 1024 * 16
        let allowedExtension = [ "txt"; "json"; "toml"; "yml"; "yaml"; "fs"; "cs" ]

        let info = FileInfo(path)

        if info.Length > sizeLimit then
            let entry = { FilePath = path; Result = Error "File to large" }
            do! onFind entry
            return ()
        elif not (List.contains (info.Extension.Substring(1)) allowedExtension) then
            let entry = { FilePath = path; Result = Error "Extension not allowed for scanning" }
            do! onFind entry
            return ()
        else
            use reader = new StreamReader(path)
            let rec loop (lineNumber: int) = future {
                let! line = reader.ReadLineAsync() |> Future.ofTask
                match line with
                | null -> return ()
                | line ->
                    let onFind column = future {
                        let res = { Line = lineNumber; Column = column; LineStr = line }
                        let entry = { FilePath = path; Result = Ok res }
                        do! onFind entry
                    }
                    do! scanString pattern onFind line
                    return! loop (lineNumber + 1)
            }
            return! loop 0
    }
