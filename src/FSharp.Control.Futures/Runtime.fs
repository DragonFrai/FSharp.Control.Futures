module FSharp.Control.Futures.Runtime

open System
open System.Collections.Generic
open System.Threading


type Spawner<'a> = IFuture<'a> -> unit

type IRuntime =
    abstract member Spawn : IFuture<'a> -> unit

type RuntimeTask<'a>(future: IFuture<'a>) =
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
             else ()
     ) |> ignore

let runOnPoolAsync (f: IFuture<'a>) : IFuture<'a> =
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

let runSync (f: IFuture<'a>) : 'a =
    use task = new RuntimeTask<'a>(f)
    spawnOnPool task
    task.ResultCell.TryWaitForResultSynchronously() |> Option.get
