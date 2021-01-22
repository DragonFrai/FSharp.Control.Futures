module Futures.Runtime

open System
open System.Collections.Generic
open System.Threading

type Spawner<'a> = Future<'a> -> unit

type IRuntime =
    abstract member Spawn : Future<'a> -> unit

//type StupidRuntime(queue: Queue<unit -> unit>, pool: ThreadPool) =
//    
//    interface IRuntime with
//        member this.Spawn(f: Future<'a>) : unit =
//            ThreadPool.QueueUserWorkItem(fun _ ->
//                let waker = fun () -> (this :> IRuntime).Spawn(f)
//                let _ = f waker
//                ()
//            ) |> ignore
//            ()

type RuntimeTask<'a>(future: Future<'a>) =
    let res: ResultCell<'a> = new ResultCell<'a>()
    let sync = obj()
    
    member _.ResultCell = res
    member _.Future = future
    member _.Sync = sync
    
    interface IDisposable with
        member _.Dispose() =
            (res :> IDisposable).Dispose()

let rec spawnOnPool (task: RuntimeTask<'a>) =
     let waker () =
        if not task.ResultCell.ResultAvailable then
            spawnOnPool task
     
     ThreadPool.QueueUserWorkItem(fun _ ->
        lock task.Sync ^fun () ->
            if not task.ResultCell.ResultAvailable then
                let result = Future.poll waker task.Future
                match result with
                | Ready x -> task.ResultCell.RegisterResultIfNotAvailable(x)
                | Pending -> ()
                | Cancelled -> failwith "TODO"
            else ()
     ) |> ignore

let runOnPoolAsync (f: Future<'a>) : Future<'a> =
    let task = new RuntimeTask<'a>(f)
    spawnOnPool task
    let innerF waker =
        lock task.Sync ^fun () ->
            if task.ResultCell.ResultAvailable
            then
                let r = task.ResultCell.GrabResult()
                (task :> IDisposable).Dispose()
                Ready r
            else
                task.ResultCell.SetOnRegisterResultNoLock(waker)
                Pending
    Future.create innerF

let runSync (f: Future<'a>) : 'a =
    use task = new RuntimeTask<'a>(f)
    spawnOnPool task
    task.ResultCell.TryWaitForResultSynchronously() |> Option.get



//    let mutable result = None
//    spawn f (fun x -> result <- Some x)
//    while Option.isNone result do ()
//    Option.get result
    