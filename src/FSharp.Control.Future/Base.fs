module FSharp.Control.Future.Base

open System.Threading
open System.Timers
open FSharp.Control.Future


[<RequireQualifiedAccess>]
module Future =

    let sleep (duration: int) =
        // if None the time out
        let mutable currentWaker = None
        let mutable timer = None
        let sync = obj()

        timer <-
            let t = new Timer(float duration)
            t.AutoReset <- false
            t.Elapsed.Add(fun _ ->
                // TODO: think!! Called from other thread
                timer <- None
                t.Dispose()
                lock sync ^fun () ->
                    match currentWaker with
                    | Some w -> w ()
                    | None -> ()
            )
            Some t
        Future ^fun waker ->
            match timer with
            | Some timer ->
                lock sync ^fun () ->
                    currentWaker <- Some waker
                    if not timer.Enabled then timer.Start()
                Pending
            | None ->
                Ready ()


    // TODO: optimize
    let parallelSeq (futures: Future<'a> seq) : Future<'a[]> =
        let mutable futures = futures |> Seq.map ValueSome |> Seq.toArray
        let mutable results: 'a[] = Array.zeroCreate (Array.length futures)

        let innerF waker =
            futures
            |> Seq.indexed
            |> Seq.map (fun (i, f) ->
                match f with
                | ValueSome f ->
                    let p = Future.poll waker f
                    match p with
                    | Ready value ->
                        futures.[i] <- ValueNone
                        results.[i] <- value
                        true
                    | Pending -> false
                | ValueNone -> true
            )
            |> Seq.reduce (&&)
            |> fun x ->
                match x with
                | false -> Pending
                | true -> Ready results
        Future innerF

    // TODO: fix it
    let run (f: Future<'a>) : 'a =
        use wh = new EventWaitHandle(false, EventResetMode.ManualReset)
        let waker () = wh.Set |> ignore

        let rec wait (current: Poll<'a>) =
            match current with
            | Ready x -> x
            | Pending ->
                wh.WaitOne() |> ignore
                wait (Future.poll waker f)

        wait (Future.poll waker f)




