# Запуск Future без среды исполнения на текущем потоке

Future можно запустить на текущем потоке используя `Future.runBlocking`.
Переданная Future запустится, а вызывающий поток будет заблокирован
пока не получится результат.
```fsharp
let fut = future {
    let! name = Future.ready "Alex"
    do! Future.sleepMs 1000
    return $"Hello, {name}!"
}

let phrase = fut |> Future.runBlocking
printfn $"{phrase}"
```
