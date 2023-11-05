namespace FSharp.Control.Futures.Scheduling

open System.Collections.Generic
open System.Linq.Expressions
open System.Runtime.CompilerServices
open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.Internals


module Constants =
    let [<Literal>] MinimalTrimDelta : int = 1024 // 4кб для int


type ISchedulerTask =
    abstract Poll: IContext -> bool
    abstract Drop: unit -> unit

type SchedulerTask<'a> =
    val mutable currentState: NaiveFuture<'a>
    val mutable isWaiting: bool // runtime safety checks for onceCell
    val mutable onceCell: PrimaryOnceCell<'a>

    new (fut: Future<'a>) = {
        currentState = NaiveFuture(fut)
        isWaiting = false
        onceCell = PrimaryOnceCell.Empty()
    }

    override this.Equals(other: obj): bool =
        refEq this other

    override this.GetHashCode(): int =
        RuntimeHelpers.GetHashCode(this)

    interface IFutureTask<'a> with
        member this.Cancel() : unit = failwith "todo"
        member this.Await() : Future<'a> = failwith "todo"
        member this.WaitBlocking() : 'a = (this:> IFutureTask<'a>).Await() |> Future.runSync

    interface Future<'a> with
        member this.Poll(ctx: IContext) : Poll<'a> =
            this.onceCell.PollGet(ctx) |> NaivePoll.toPoll

        member _.Drop() : unit =
            failwith "TODO"


[<Struct; NoComparison; StructuralEquality>]
type TaskId = TaskId of int
    with member this.Inner() : int = let (TaskId x) = this in x

type SingleThreadScheduler() =
    let syncObj = obj
    let mutable disposeCancellationToken = CancellationToken()

    let mutable tasks = List<ISchedulerTask>()
    let mutable pollingQueue = Queue<ISchedulerTask>()



module SingleThreadScheduler =
    ()
