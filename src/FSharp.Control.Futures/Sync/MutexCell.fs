namespace FSharp.Control.Futures.Sync

open FSharp.Control.Futures


[<Class>]
type MutexCell<'a> =

    val private mutex: Mutex

    val mutable private value: 'a


    new(value: 'a) = { mutex = Mutex(); value = value }


    member this.ValueUnchecked
        with get () : 'a = this.value
        and set (value: 'a) : unit = this.value <- value

    /// <summary>
    /// Returns value.
    /// Guarantees that no other thread changes the value at the moment it is taken.
    /// May be useful for non atomically struct data types.
    ///
    /// But if you went change value, keep in mid that other thread may
    /// get value too, while you change value and going use it in `mutex.Set(x)` call.
    /// </summary>
    member this.Get(): Future<'a> = future {
        do! this.mutex.Lock()
        let r = this.value
        do this.mutex.Unlock()
        return r
    }

    member this.Set(value: 'a): Future<unit> = future {
        do! this.mutex.Lock()
        do this.value <- value
        do this.mutex.Unlock()
        return ()
    }

    /// <summary>
    /// Like `.Get` function, but can extract part of value
    /// </summary>
    /// <param name="reader"></param>
    member this.Lock(f: 'a -> 'b): Future<'b> = future {
        do! this.mutex.Lock()
        let r = f this.value
        do this.mutex.Unlock()
        return r
    }

    member this.LockIn(f: 'a -> Future<'b>): Future<'b> = future {
        do! this.mutex.Lock()
        let! r = f this.value
        do this.mutex.Unlock()
        return r
    }

    member this.Update(updater: 'a -> 'a): Future<unit> = future {
        do! this.mutex.Lock()
        let newValue = updater this.value
        do this.value <- newValue
        do this.mutex.Unlock()
        return ()
    }

    member this.UpdateIn(updater: 'a -> Future<'a>): Future<unit> = future {
        do! this.mutex.Lock()
        let! newValue = updater this.value
        do this.value <- newValue
        do this.mutex.Unlock()
        return ()
    }

    member this.UpdateWith(updater: 'a -> 'a * 'b): Future<'b> = future {
        do! this.mutex.Lock()
        let newValue, r = updater this.value
        do this.value <- newValue
        do this.mutex.Unlock()
        return r
    }

    member this.UpdateWithIn(updater: 'a -> Future<'a * 'b>): Future<'b> = future {
        do! this.mutex.Lock()
        let! newValue, r = updater this.value
        do this.value <- newValue
        do this.mutex.Unlock()
        return r
    }

    member this.Mutate(mutator: 'a -> unit): Future<unit> = future {
        do! this.mutex.Lock()
        do mutator this.value
        do this.mutex.Unlock()
        return ()
    }

    member this.MutateIn(mutator: 'a -> Future<unit>): Future<unit> = future {
        do! this.mutex.Lock()
        do! mutator this.value
        do this.mutex.Unlock()
        return ()
    }


[<RequireQualifiedAccess>]
module MutexCell =

    let inline create<'a> (initial: 'a) : MutexCell<'a> =
        MutexCell(initial)

    let inline get (mCell: MutexCell<'a>) : Future<'a> =
        mCell.Get()

    let inline set (value: 'a) (mCell: MutexCell<'a>) : Future<unit> =
        mCell.Set(value)

    let inline lock (reader: 'a -> 'b) (mCell: MutexCell<'a>) : Future<'b> =
        mCell.Lock(reader)

    let inline lockIn (reader: 'a -> Future<'b>) (mCell: MutexCell<'a>) : Future<'b> =
        mCell.LockIn(reader)

    let inline update (updater: 'a -> 'a) (mCell: MutexCell<'a>) : Future<unit> =
        mCell.Update(updater)

    let inline updateIn (updater: 'a -> Future<'a>) (mCell: MutexCell<'a>) : Future<unit> =
        mCell.UpdateIn(updater)

    let inline updateWith (updater: 'a -> 'a * 'b) (mCell: MutexCell<'a>) : Future<'b> =
        mCell.UpdateWith(updater)

    let inline updateWithIn (updater: 'a -> Future<'a * 'b>) (mCell: MutexCell<'a>) : Future<'b> =
        mCell.UpdateWithIn(updater)

    let inline mutate (mutator: 'a -> unit) (mCell: MutexCell<'a>) : Future<unit> =
        mCell.Mutate(mutator)

    let inline mutateIn (mutator: 'a -> Future<unit>) (mCell: MutexCell<'a>) : Future<unit> =
        mCell.MutateIn(mutator)
