namespace FSharp.Control.Future

open System
open System.Threading

[<Struct>]
type Poll<'a> =
    | Ready of 'a // Completed
    | Pending     // Incomplete
    | Cancelled   // Completed

module Poll =
    
    let ready x = Ready x
    
    let bind binding = function
        | Ready x -> binding x
        | Pending -> Pending
        | Cancelled -> Cancelled
    
    let map mapping = bind (mapping >> ready)
    
    let join poll = poll |> bind id

    let tryReady poll =
        match poll with
        | Ready x -> Some x
        | _ -> None
    
type Waker = unit -> unit

[<Struct>]
type Future<'a> = Future of (Waker -> Poll<'a>)

type CancellableFuture<'a> = CancellationToken -> Future<'a>

exception FutureCancelledException of string

[<RequireQualifiedAccess>]
module Future = 
    
    let create f = Future f
    
    let poll waker (Future f) = f waker
    
    let single value : Future<'a> =
        create ^fun _ -> value |> Ready
    
    let singlePoll poll = create (fun _ -> poll)
    
    let never<'a> : Future<'a> =
        create ^fun _ -> Pending
    
    let cancelled<'a> : Future<'a> =
        create ^fun _ -> Cancelled
    
//    [<Struct>]
//    type private BindState<'a, 'b> =
//        | First of 'a
//        | Second of 'b
    
    let bind (binding: 'a -> Future<'b>) (fa: Future<'a>) : Future<'b> =
        let mutable stateA = ValueSome fa
        let mutable (stateB: Future<'b> voption) = ValueNone
        
        let innerF waker =
            match stateB with
            | ValueSome fb -> poll waker fb
            | ValueNone ->
                match stateA with
                | ValueSome fa ->
                    match poll waker fa with
                    | Ready x ->
                        let fb = binding x
                        stateB <- ValueSome fb
                        stateA <- ValueNone
                        poll waker fb
                    | Pending -> Pending
                    | Cancelled -> Cancelled
                | ValueNone -> invalidOp "Unreachable"
        
        create innerF

    let join (f: Future<Future<'a>>) : Future<'a> = bind id f
    
    // TODO: optimize
    let parallelSeq (futures: Future<'a> seq) : Future<'a[]> =
        let mutable futures = futures |> Seq.map (fun f -> ValueSome f) |> Seq.toArray
        // Must be filled !!!
        let mutable results: 'a[] = Array.create (Array.length futures) (Unchecked.defaultof<_>)
        let mutable isCancelled = false
        
        let innerF waker =
            futures
            |> Seq.indexed
            |> Seq.map (fun (i, f) ->
                match f with
                | ValueSome f ->
                    //printfn "\t %i" i 
                    let p = poll waker f
                    match p with
                    | Ready value ->
                        futures.[i] <- ValueNone
                        results.[i] <- value
                        true
                    | Pending -> false
                    | Cancelled -> isCancelled <- true; true
                | ValueNone -> true
            )
            |> Seq.reduce (&&)
            |> fun x ->
                match x, isCancelled with
                | _, true -> Cancelled
                | false, _ -> Pending
                | true, _ -> Ready results
        create innerF
    
//    let parallelSeq (futures: Future<'a> seq) : Future<'a[]> =
//        let mutable futures = futures |> Seq.map (fun f -> ValueSome f) |> Seq.toArray
//        // Must be filled !!!
//        let mutable results: 'a[] = Array.create (Array.length futures) (Unchecked.defaultof<_>)
//        let mutable isCancelled = false
//        
//        let innerF waker =
//            futures
//            |> Seq.indexed
//            |> Seq.map (fun (i, f) ->
//                match f with
//                | ValueSome f ->
//                    //printfn "\t %i" i 
//                    let p = poll waker f
//                    match p with
//                    | Ready value ->
//                        futures.[i] <- ValueNone
//                        results.[i] <- value
//                        true
//                    | Pending -> false
//                    | Cancelled -> isCancelled <- true; true
//                | ValueNone -> true
//            )
//            |> Seq.reduce (&&)
//            |> fun x ->
//                match x, isCancelled with
//                | _, true -> Cancelled
//                | false, _ -> Pending
//                | true, _ -> Ready results
//        create innerF

    
    let cancellable future = fun (ct: CancellationToken) ->
        create ^fun waker ->
            if ct.IsCancellationRequested
            then Cancelled
            else poll waker future

[<AutoOpen>]
module FutureExt = 
    
    [<RequireQualifiedAccess>]
    module Future =
        
        // TODO: fix it
        let _run (f: Future<'a>) : 'a =
            use wh = new EventWaitHandle(false, EventResetMode.ManualReset)
            let waker () = wh.Set |> ignore
            
            let rec wait (current: Poll<'a>) =
                match current with
                | Ready x -> x
                | Pending ->
                    wh.WaitOne() |> ignore
                    wait (Future.poll waker f)
                | Cancelled -> raise (FutureCancelledException "Future was cancelled")
            
            wait (Future.poll waker f)

        
        let getWaker = Future.create (fun w -> Ready w)
        
        
        let sleep (duration: int) =
            // if None the time out
            let mutable currentWaker = None
            let mutable timer = None
            let sync = obj()
            
            timer <- 
                let t = new Timers.Timer(float duration)
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
            
            Future.create ^fun waker ->
                match timer with
                | Some timer ->
                    lock sync ^fun () ->
                        currentWaker <- Some waker
                        if not timer.Enabled then timer.Start()
                    Pending
                | None ->
                    Ready ()