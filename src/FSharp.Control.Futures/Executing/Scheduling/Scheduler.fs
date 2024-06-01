namespace rec FSharp.Control.Futures.Executing.Scheduling

open System
open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.Executing
open FSharp.Control.Futures.Internals



// [ Scheduling ]

// proxy for calling task method for processing work from scheduler
// Я пытался сделать это абстрактным классом. Честно. Я прогирал F#'у
type [<Interface>] ITaskSchedulerAgent =
    abstract Id: FutureTaskId

    /// <summary>
    /// Производит работу, которая была запрошена через <c>Context.Wake</c>, <c>IFutureTask.Abort</c>
    /// или сразу после <c>Executor.Spawn</c> (<c>Poll</c>'ит внутреннию <c>Future</c> или <c>Drop</c>'ает её.)
    /// </summary>
    /// <returns>
    /// true, если работа была выполнена
    /// (Опрос <c>Future</c> завершился Ready, или с исключением, или произошел <c>Drop</c>)
    /// </returns>
    abstract DoWork: unit -> bool

    /// <summary>
    /// Принудительно отбрасывает результат работы прямо сейчас (при условии что задача еще не завершена).
    /// Ожидается, что эта функция будет вызываться планировщиком.
    /// </summary>
    /// <param name="reason"> Исключение которое будет подставлено вместо результата. </param>
    abstract DropWork: reason: exn -> unit

/// <summary>
/// Интерфейс для упрощенной реализации Executor'ов.
/// Используется в тандеме с SchedulerTask,
/// которая поддерживает некоторые инварианты для упрощения реализации планировщика.
/// А именно, невозможностью поставить SchedulerTask в очередь дважды.
/// </summary>
type [<Interface>] IScheduler =
    inherit IDisposable

    /// <summary>
    /// Принимает Future и возвращает таску.
    /// По большому счету создает SchedulerTask с этим IScheduler экземпляром
    /// и опционально дает планировщику возможность изменить свои внутренние структуры.
    /// </summary>
    abstract Spawn: Future<'a> -> SchedulerTask<'a>

    /// <summary>
    /// Говорит <c>IScheduler</c> поставить задачу в очередь на исполнение.
    /// </summary>
    abstract Schedule: ITaskSchedulerAgent -> unit

module internal TaskState =

    let [<Literal>] Initial = 0

    /// When true, task already queued
    /// Устанавливается в 1 когда появляется запрос на пробуждение Future,
    /// устанавливающий обязан уведомить об этом Scheduler.
    /// Поток планировщика, который начал совершать работу, должен
    /// сбросить этот бит в 0, установив <c>WorkBit</c> в 1, чтобы последующие Wake'и
    /// не добавляли Future в очередь, пока рабочий поток не закончит опрос.
    /// По завершении опроса рабочий поток должен проверить WakeBit для
    /// фактического передобавления в очередь планировщика.
    let [<Literal>] WakeBit = 1 // 1 <<< 1

    /// When true, task executing by scheduler
    /// Устанавливается в 1, когда рабочий поток начинает опрос Future.
    /// Должен быть сброшен в 0 всякий раз, когда Future не занят поток планировщика.
    let [<Literal>] WorkBit = 2 // 1 <<< 2

    /// When true, abort was requested from IFutureTask
    /// Установка в 1 переопределяет тип работы, которую совершает рабочий поток над Future.
    /// Вместо опроса должен будет вызываться Drop.
    let [<Literal>] AbortBit = 4 // 1 <<< 3

    /// When true, task already completed with result
    let [<Literal>] Completed = 8 // 1 <<< 4

    let inline isWake x = x ||| WakeBit <> 0
    let inline isWork x = x ||| WorkBit <> 0
    let inline isAbort x = x ||| AbortBit <> 0
    let inline isComplete x = x ||| Completed <> 0

/// <summary>
/// Пассивная единица выполнения работы.
/// Представляет собой "ячейку" с результатом, который вычисляет сама используя внутреннию Future.
/// Однако требует продвижения через <c>PollWork</c> и <c>DropWork</c>
/// </summary>
type SchedulerTask<'a> =
    val mutable state: int

    // [ ITaskSchedulerAgent + IContext part ]
    val mutable scheduler: IScheduler
    val mutable taskId: FutureTaskId // Id on current scheduler
    val mutable work: NaiveFuture<'a>

    // [ IFutureTask part ]
    val mutable isWaiting: bool // runtime safety checks for single awaiting
    val mutable isAborted: bool // runtime safety checks for single aborting

    // [ Future part ]
    val mutable resultCell: PrimaryOnceCell<ExnResult<'a>>

    new (scheduler: IScheduler, id: FutureTaskId, fut: Future<'a>) =
        { state = TaskState.Initial
          scheduler = scheduler
          taskId = id
          work = NaiveFuture(fut)
          isWaiting = false
          isAborted = false
          resultCell = PrimaryOnceCell.Empty() }

    interface IContext with
        member this.Wake() =
            let mutable doLoop = true
            let mutable cState = this.state
            while doLoop do
                match cState with
                | s when TaskState.isComplete s -> failwith "Awakening of an already completed Future"
                | s when (s &&& TaskState.WakeBit) = 0 ->
                    let newState = cState ||| TaskState.WakeBit
                    let exchangedState = Interlocked.CompareExchange(&this.state, newState, cState)
                    if exchangedState <> cState
                    then cState <- exchangedState
                    else
                        // Если нет рабочего потока который прямо сейчас совершает работу,
                        // то добавляем задачу в очередь планировщика
                        if cState &&& TaskState.WorkBit = 0 then
                            this.scheduler.Schedule(this)
                        doLoop <- false
                | s -> () // WakeBit <> 0, skip

    interface ITaskSchedulerAgent with

        member this.Id = this.taskId

        member this.DropWork(ex) =
            try
                this.work.Drop()
            with _dropEx ->
                this.resultCell.Put(ExnResult<'a>.Exn(ex))

        member this.DoWork(): bool =
            // 1. Устанавливаем бит совершения работы, чтобы предотвратить дублирующее уведомление планировщика
            let doAbort =
                let mutable doLoop = true
                let mutable cState = this.state
                let mutable r = false
                while doLoop do
                    match cState with
                    | s when s &&& TaskState.WorkBit <> 0 -> failwith "Scheduler already polling Future"
                    | s when s &&& TaskState.WakeBit = 0 -> failwith "DoWork called on not waked Future"
                    | _ ->
                        let newState = cState ^^^ (TaskState.WorkBit ||| TaskState.WakeBit)
                        let exchangedState = Interlocked.CompareExchange(&this.state, newState, cState)
                        if exchangedState <> cState
                        then cState <- exchangedState
                        else
                            r <- cState ||| TaskState.AbortBit <> 0
                            doLoop <- false
                r

            // 2. Производим Poll или Drop внутренней Future.
            let isCompleted =
                if not doAbort then
                    try
                        let poll = this.work.Poll(this)
                        match poll with
                        | NaivePoll.Pending -> false
                        | NaivePoll.Ready x ->
                            this.resultCell.Put(ExnResult<'a>.Ok(x))
                            true
                    with ex ->
                        this.resultCell.Put(ExnResult<'a>.Exn(ex))
                        true
                else
                    try
                        this.work.Drop()
                        this.resultCell.Put(ExnResult<'a>.Exn(FutureTaskAbortedException()))
                        true
                    with ex ->
                        this.resultCell.Put(ExnResult<'a>.Exn(ex))
                        true

            // 3. Сбрасываем WorkBit в 0 и пробуждаем Future если в процессе работы был установлен WakeBit
            do
                let mutable doLoop = true
                let mutable cState = this.state
                while doLoop do
                    match cState with
                    | s when s &&& TaskState.WorkBit = 0 -> failwith ">_<"
                    | _ ->
                        if isCompleted then
                            let newState = TaskState.Completed
                            let exchangedState = Interlocked.CompareExchange(&this.state, newState, cState)
                            if exchangedState <> cState
                            then cState <- exchangedState
                            else doLoop <- false
                        else
                            let newState = cState &&& ~~~TaskState.WorkBit
                            let exchangedState = Interlocked.CompareExchange(&this.state, newState, cState)
                            if exchangedState <> cState
                            then cState <- exchangedState
                            else
                                if cState &&& TaskState.WakeBit <> 0 then
                                    this.scheduler.Schedule(this)
                                doLoop <- false

            // return
            isCompleted

    interface IFutureTask<'a> with

        member this.Await() : Future<'a> =
            if this.isWaiting then invalidOp "Unable wait IFutureTask twice. You can consider various synchronization primitives or channels."
            this.isWaiting <- true
            this

        member this.Abort() : unit =
            if this.isAborted then invalidOp "Unable abort IFutureTask twice"
            this.isAborted <- true
            let mutable doLoop = true
            let mutable cState = this.state
            while doLoop do
                match cState with
                | s when TaskState.isComplete s -> ()
                | s when s &&& TaskState.AbortBit <> 0 -> failwith "Abort twice"
                | s ->
                    let newState = cState ||| (TaskState.WakeBit ||| TaskState.AbortBit)
                    let exchangedState = Interlocked.CompareExchange(&this.state, newState, cState)
                    if exchangedState <> cState
                    then cState <- exchangedState
                    else
                        if (cState &&& TaskState.WorkBit = 0) && (cState &&& TaskState.WakeBit = 0) then
                            this.scheduler.Schedule(this)
                        doLoop <- false

    // Calling by waiter
    interface Future<'a> with

        member this.Poll(ctx: IContext) : Poll<'a> =
            // Опрашиваем ячейку с результатом и если в ней исключение,
            // выкидываем его
            this.resultCell.Poll(ctx) |> NaivePoll.toPollExn

        member this.Drop() : unit =
            // Очищаем состояние resultCell и делаем её
            // заблокированной для повторного использования
            this.resultCell.Drop()


// [ SchedulerExecutor ]
type SchedulerExecutor(scheduler: IScheduler) =
    let mutable isDisposed = 0

    interface IExecutor with
        member this.Dispose() =
            let mutable doLoop = true
            while doLoop do
                if isDisposed <> 0 then raise (ObjectDisposedException("Executor"))
                if Interlocked.CompareExchange(&isDisposed, 1, 0) = 0 then
                    doLoop <- false
            scheduler.Dispose()

        member this.Spawn(fut) =
            scheduler.Spawn(fut)
