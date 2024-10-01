namespace FSharp.Control.Futures.Sync

open FSharp.Control.Futures


[<Class>]
[<Sealed>]
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
    /// <param name="f"></param>
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

    member this.Update(f: 'a -> 'a): Future<unit> = future {
        do! this.mutex.Lock()
        let newValue = f this.value
        do this.value <- newValue
        do this.mutex.Unlock()
        return ()
    }

    member this.UpdateIn(f: 'a -> Future<'a>): Future<unit> = future {
        do! this.mutex.Lock()
        let! newValue = f this.value
        do this.value <- newValue
        do this.mutex.Unlock()
        return ()
    }

    member this.UpdateWith(f: 'a -> 'a * 'b): Future<'b> = future {
        do! this.mutex.Lock()
        let newValue, r = f this.value
        do this.value <- newValue
        do this.mutex.Unlock()
        return r
    }

    member this.UpdateWithIn(f: 'a -> Future<'a * 'b>): Future<'b> = future {
        do! this.mutex.Lock()
        let! newValue, r = f this.value
        do this.value <- newValue
        do this.mutex.Unlock()
        return r
    }

    member this.Mutate(f: 'a -> unit): Future<unit> = future {
        do! this.mutex.Lock()
        do f this.value
        do this.mutex.Unlock()
        return ()
    }

    member this.MutateIn(f: 'a -> Future<unit>): Future<unit> = future {
        do! this.mutex.Lock()
        do! f this.value
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

    let inline lock (f: 'a -> 'b) (mCell: MutexCell<'a>) : Future<'b> =
        mCell.Lock(f)

    let inline lockIn (f: 'a -> Future<'b>) (mCell: MutexCell<'a>) : Future<'b> =
        mCell.LockIn(f)

    let inline update (f: 'a -> 'a) (mCell: MutexCell<'a>) : Future<unit> =
        mCell.Update(f)

    let inline updateIn (f: 'a -> Future<'a>) (mCell: MutexCell<'a>) : Future<unit> =
        mCell.UpdateIn(f)

    let inline updateWith (f: 'a -> 'a * 'b) (mCell: MutexCell<'a>) : Future<'b> =
        mCell.UpdateWith(f)

    let inline updateWithIn (f: 'a -> Future<'a * 'b>) (mCell: MutexCell<'a>) : Future<'b> =
        mCell.UpdateWithIn(f)

    let inline mutate (f: 'a -> unit) (mCell: MutexCell<'a>) : Future<unit> =
        mCell.Mutate(f)

    let inline mutateIn (f: 'a -> Future<unit>) (mCell: MutexCell<'a>) : Future<unit> =
        mCell.MutateIn(f)
