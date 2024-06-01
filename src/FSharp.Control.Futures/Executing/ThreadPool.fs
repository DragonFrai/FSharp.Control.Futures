namespace rec FSharp.Control.Futures.Scheduling
//
// open System.Collections.Generic
// open FSharp.Control.Futures.Executing
// open FSharp.Control.Futures.Executing.Scheduling
//
//
// module internal ThreadPoolScheduler =
//     type internal Action =
//         | Work of ITaskSchedulerAgent
//         | Abort of ITaskSchedulerAgent
//
//     /// <summary>
//     /// </summary>
//     type [<Struct>] TaskState =
//         | Sleep
//         | WaitWork
//         | WaitAbort
//
//     type ThreadPoolScheduler internal () =
//
//         let syncObj = obj()
//         let tasks: Dictionary<TaskId, ITaskSchedulerAgent * TaskState> = Dictionary()
//         let touch: HashSet<TaskId> = HashSet()
//
//         static member internal Instance = new ThreadPoolScheduler()
//
//         member this.DoWork() =
//             let task = lock syncObj <| fun () ->
//                 let task = touch |> Seq.tryHead
//
//
//                 failwith "TODO"
//             ()
//
//         interface IScheduler with
//
//             member this.Dispose() = invalidOp "Impossible dispose global thread pool based scheduler"
//
//             member this.Spawn(fut) =
//                 let mutable task = Unchecked.defaultof<_>
//                 lock syncObj <| fun () ->
//                     let mutable doLoop = true
//                     while doLoop do
//                         let tid = TaskId.generate ()
//                         if not (tasks.ContainsKey(tid)) then
//                             task <- SchedulerTask(this, tid, fut)
//                             tasks[tid] <- (task, TaskState.WaitWork)
//                             touch.Add(tid) |> ignore
//                             doLoop <- false
//                 task
//
//             member this.Abort(task) =
//                 let tid = task.Id
//                 lock syncObj <| fun () ->
//                     let agent, state = tasks[tid]
//                     match state with
//                     | Sleep | WaitWork ->
//                         tasks[tid] <- (task, TaskState.WaitAbort)
//                         touch.Add(tid) |> ignore
//                     | WaitAbort ->
//                         invalidOp "Double future task aborting"
//
//             member this.Revive(task) =
//                 let tid = task.Id
//                 lock syncObj <| fun () ->
//                     let agent, state = tasks[tid]
//                     match state with
//                     | Sleep ->
//                         tasks[tid] <- (task, TaskState.WaitWork)
//                         touch.Add(tid) |> ignore
//                     | WaitWork -> ()
//                     | WaitAbort ->
//                         invalidOp "Scheduling aborted future task"
//
//
// type ThreadPoolExecutor internal () =
//     static member Instance: IExecutor = new ThreadPoolExecutor()
//     interface IExecutor with
//         member this.Dispose() = invalidOp "Impossible dispose global thread pool based executor"
//         member this.Spawn(fut) =
//             (ThreadPoolScheduler.ThreadPoolScheduler.Instance :> IScheduler).Spawn(fut)
//
// module ThreadPoolExecutor =
//     let spawn fut = ThreadPoolExecutor.Instance.Spawn(fut)
