module FSharp.Control.Futures.Playground.RuntimeExamples

open FSharp.Control.Futures
open FSharp.Control.Futures.Runtime.ThreadPoolRuntime




let simpleExample () =
    let rt = ThreadPoolRuntime.Instance

    let newFuture = fun (name: string) -> future {
        System.Console.WriteLine($"{name}: Future begin")
        do! Future.yieldWorkflow ()
        System.Console.WriteLine($"{name}: Some work...")
        do! Future.sleepMs 1000
        System.Console.WriteLine($"{name}: Work completed")
        do! Future.yieldWorkflow ()
        System.Console.WriteLine($"{name}: Future end")
        return name
    }

    let ft1 = rt.Spawn(newFuture "<1>")
    let ft2 = rt.Spawn(newFuture "<2>")

    let r1 = ft1.Await() |> Future.runBlocking
    let r2 = ft2.Await() |> Future.runBlocking

    printfn $"%A{r1}, %A{r2}"

    ()




