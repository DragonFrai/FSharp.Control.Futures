# Создание Future используя Future CE

Future имеет свой CE, который используется также как async или task CE встроенные в F#.
Более подробно о CE вы можете прочитать на [сайте](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/computation-expressions).

Например, мы можем заменить базовые функции создания на future CE:
```fsharp
let ready = future { return "Hello, world!" } // ~ Future.ready "Hello, world!"
let lazy' = future { return (foo ()) } // ~ Future.lazy' (fun () -> foo ())
```

Наиболее важным свойством CE является упрощение работы с bind.
Пример чтения-записи можно переписать используя CE так:

```fsharp
// readFileAsync: filePath: string -> Future<string>
// writeFileAsync: filePath: string -> content: string -> Future<unit>
let readAndWriteFuture = futur {
    let! content = readFileAsync "my-file.txt"
    return! writeFileAsync "other-file.txt" content
}
```

Видимым преимуществом CE является возможность "уплощить" цепочка bind, зависимых между собой.
Пример множественно зависимых bind можно переписать так:

```fsharp
let doManyWorkWithCrossResults = future {
    let! val1 = doWork1 ()
    let! val2 = doWork2 val1
    let! val3 = doWork3 val1 val2
    ...
    let! valN = doWorkN val1 val2 ... valPrevN
}
```

Также CE добавляют синтаксис и для Future.merge или Future.catch комбинаторов.

```fsharp
let parallelCE = future {
    let! val1 = doWork1 ()
    and! val2 = doWork2 ()
    and! val3 = doWork3 ()
}
```

```fsharp
let catchCE = future {
    try
        do! doWork ()
    with ex ->
        printfn $"{ex}"
}
```

```fsharp
let tryFinally = future {
    try
        do! doWork ()
    finally
        do finallize ()
}
```
