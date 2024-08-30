namespace rec FSharp.Control.Futures.Sync

open System
open System.Diagnostics
open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel


exception SemaphoreClosedException
exception SemaphorePermitsOverflowException


type internal SemaphoreAcquire =
    inherit IntrusiveNode<SemaphoreAcquire>

    val semaphore: Semaphore
    val permits: int
    val mutable queued: bool
    val mutable primaryNotify: PrimaryNotify

    new(semaphore: Semaphore, permits: int) =
        { semaphore = semaphore
          permits = permits
          queued = false
          primaryNotify = PrimaryNotify(false) }

    interface IFuture<unit> with
        member this.Poll(ctx) =
            this.semaphore.PollAcquire(this, ctx)
        member this.Drop() =
            this.semaphore.DropAcquire(this)



[<Struct>]
type internal SemaphoreState =
    // Contains non-negative permits count or -1 for closed state
    val mutable state: int
    new(state: int) = { state = state }

    static member inline WithPermits(permits: int): SemaphoreState =
        if permits < 0 then raise SemaphorePermitsOverflowException
        SemaphoreState(permits)

    member inline this.CompareExchange(value: SemaphoreState, comparand: SemaphoreState): SemaphoreState =
        SemaphoreState(Interlocked.CompareExchange(&this.state, value.state, comparand.state))

    member inline this.AsInt: int =
        this.state

    member inline this.IsClosed: bool =
        this.state = -1

    member inline this.AssertNotClosed(): unit =
        if this.IsClosed then raise SemaphoreClosedException

    member inline this.AssertNotClosedPipe(): SemaphoreState =
        this.AssertNotClosed()
        this

    member inline this.Permits: int =
        this.state

    member inline this.AddPermits(permits: int): SemaphoreState =
        let permits = this.state + permits
        if permits < 0 then raise SemaphorePermitsOverflowException
        else SemaphoreState(permits)

    member inline this.Close(): SemaphoreState =
        SemaphoreState(-1)


// TODO?: Поддержка разных режимов порядка Fifi/Lifo/Drain
//        Предложение соблазнительное. Но как минимум потребует усложнений хранения состояний,
//        Разделяемое состояние больше не сможет быть либо числом в диапазоне [0..Int32.MaxValue], либо -1 (Closed)
//        Вернее, некоторые оптимизации не смогут быть реализованы без лока, только атомарными операциями,
//        для различных режимов.
//        1. Можно просто уменьшить количество допустимых permits, взяв часть бит под доп состояние
//           Цена - дополнительные операции при работе с состоянием.
//           Один из обходных вариантов - использование отрицательного диапазона
//           [0..-Int32.MaxValue] вместо дополнительного бита для дополнительного функционала (обратите внимание на 0) и
//           значение Int32.MinValue для хранения состояния Closed.
//           Это использует операции завязанные на дополнительном коде чисел, т.к. нужен будет abs(int).
//           Что тоже является усложнением. Стоит ли это замедления скорости (околонулевого)?
//        2. Реализуйте несколько классов Semaphore, вместо того чтобы параметризовать один.
//           Это позволит лучше оптимизировать их.
//           Но реализовывать их независимо без кучи разделяемого кода будет пыткой.
//           И все равно все придет к кастомизации бит в состояниях если они потребуются.
//
//        Считаю оптимальным либо оставить только Fifo режим, более строгий Drain (гарантирующий отсутствие
//        ожиданий семафора, если у семафора permits >= запрашиваемых, и ожидание остальных).
//        Либо параметризировать единую реализацию семафора.
//        Или не гарантировать порядок вообще и пропускать меньшие запросы на ресурсы мимо очереди если они выполнимы.
//
// [<Struct>]
// type SemaphoreMode =
//     | Fifo
//     | Lifo
//     | Drain

// TODO: Гарантировать порядок Fifi.
//        Сейчас если пришел мелкий запрос на ресурсы после большого поставленного в очередь,
//        мелкий будет удовлетворен независимо от наличия большего в очереди.
//        Альтернатива: Вообще не думать о гарантировании порядка.

/// <summary>
/// Async Semaphore implementation.
/// </summary>
type Semaphore =

    val mutable internal state: int // Semaphore state
    val internal syncObj: obj
    val mutable internal acquiresQueue: IntrusiveList<SemaphoreAcquire>

    static member inline MaxPermits: int = Int32.MaxValue

    private new(state: SemaphoreState) =
        if state.IsClosed then
            { syncObj = nullObj
              state = state.AsInt
              acquiresQueue = IntrusiveList.Create() }
        else
            { syncObj = obj ()
              state = state.AsInt
              acquiresQueue = IntrusiveList.Create() }

    new(initialPermits: int) =
        Semaphore(SemaphoreState.WithPermits(initialPermits))

    // <Internal>

    member internal this.PollAcquire(acquire: SemaphoreAcquire, ctx: IContext): Poll<unit> =
        lock this.syncObj ^fun () ->
            if not acquire.queued then
                let state = SemaphoreState(this.state)
                state.AssertNotClosed()
                if state.Permits >= acquire.permits then
                    this.state <- state.AddPermits(-acquire.permits).AsInt
                    acquire.queued <- true
                    acquire.primaryNotify.Notify() |> ignore
                else
                    acquire.queued <- true
                    this.acquiresQueue.PushBack(acquire)

        if acquire.primaryNotify.Poll(ctx)
        then
            let state = SemaphoreState(this.state)
            if state.IsClosed
            then raise SemaphoreClosedException
            else Poll.Ready ()
        else Poll.Pending

    member internal this.DropAcquire(acquire: SemaphoreAcquire): unit =
        if not acquire.queued then
            ()
        else
            lock this.syncObj ^fun () ->
                // Acquire future wait permits. Adding permits not required
                this.acquiresQueue.Remove(acquire) |> ignore

    member internal this.ReleasePermits(permits: int) =
        if permits = 0 then ()
        else
            lock this.syncObj ^fun () ->
                this.state <- SemaphoreState(this.state).AssertNotClosedPipe().AddPermits(permits).AsInt

                while (isNotNull this.acquiresQueue.startNode) && this.state >= this.acquiresQueue.startNode.permits do
                    let next = this.acquiresQueue.PopFront()
                    this.state <- this.state - next.permits
                    do next.primaryNotify.Notify() |> ignore

    // </Internal>

    member this.AvailablePermits: int =
        SemaphoreState(this.state).AssertNotClosedPipe().Permits

    member this.TryAcquire(permits: int): bool =
        if permits = 0 then true
        else
        lock this.syncObj ^fun () ->
            let state = SemaphoreState(this.state)
            state.AssertNotClosed()
            if state.Permits < permits then
                false
            else
                this.state <- state.AddPermits(-permits).AsInt
                true

    member this.TryAcquire(): bool =
        this.TryAcquire(1)

    /// <summary>
    /// Decrease a semaphore permits by maximum of `permits`.
    /// If it’s not possible to reduce by `permits`,
    /// reduce the number of permits to 0 and returns their number.
    /// </summary>
    /// <param name="permits"> Maximum of decreased permits </param>
    /// <returns> Number of permits that were actually reduced </returns>
    member this.AcquireUp(permits: int): int =
        failwith "TODO"

    member this.Acquire(permits: int): Future<unit> =
        Trace.Assert(permits <= Semaphore.MaxPermits, "MaxPermits has been exceeded")
        SemaphoreAcquire(this, permits)

    member this.Acquire(): Future<unit> =
        this.Acquire(1)

    member this.Release(permits: int): unit =
        this.ReleasePermits(permits)

    member this.Release(): unit =
        this.Release(1)

    member this.Close(): unit =
        if SemaphoreState(this.state).IsClosed then ()
        else
            let acquireQueue =
                lock this.syncObj ^fun () ->
                    this.state <- SemaphoreState(this.state).Close().AsInt
                    this.acquiresQueue.Drain()
            acquireQueue |>
            IntrusiveNode.forEach (fun acquire -> acquire.primaryNotify.Notify() |> ignore)


module Semaphore =
    let inline create (initialPermits: int) : Semaphore = Semaphore(initialPermits)
    let inline availablePermits (semaphore: Semaphore) : int = semaphore.AvailablePermits
    let inline acquire (semaphore: Semaphore) : Future<unit> = semaphore.Acquire()
    let inline acquireMany (permits: int) (semaphore: Semaphore) : Future<unit> = semaphore.Acquire(permits)
    let inline tryAcquire (semaphore: Semaphore) : bool = semaphore.TryAcquire()
    let inline tryAcquireMany (permits: int) (semaphore: Semaphore) : bool = semaphore.TryAcquire(permits)
    let inline release (semaphore: Semaphore) : unit = semaphore.Release()
    let inline releaseMany (permits: int) (semaphore: Semaphore) : unit = semaphore.Release(permits)
    let inline close (semaphore: Semaphore) : unit = semaphore.Close()
