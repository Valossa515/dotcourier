<p align="center">
  <img src="https://raw.githubusercontent.com/Valossa515/dotcourier/main/assets/dotcourier-logo.png" alt="Logo do DotCourier" width="600"/>
</p>

<h1 align="center">DotCourier</h1>

<p align="center">
  🚀 <strong>CQRS + Mediator para .NET, com tudo incluso.</strong><br/>
  API compatível com o MediatR e os pipeline behaviors que você teria que escrever na mão. Licença MIT.
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/DotCourier"><img src="https://img.shields.io/nuget/v/DotCourier?logo=nuget&label=nuget&color=512BD4" alt="NuGet"/></a>
  <a href="https://www.nuget.org/packages/DotCourier"><img src="https://img.shields.io/nuget/dt/DotCourier?logo=nuget&label=downloads&color=512BD4" alt="Downloads no NuGet"/></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-8%20%7C%2010-512BD4?logo=dotnet" alt=".NET 8 | 10"/></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="Licença: MIT"/></a>
</p>

<p align="center">
  <a href="https://github.com/Valossa515/dotcourier/actions/workflows/ci.yml"><img src="https://github.com/Valossa515/dotcourier/actions/workflows/ci.yml/badge.svg" alt="CI"/></a>
  <a href="https://github.com/Valossa515/dotcourier/stargazers"><img src="https://img.shields.io/github/stars/Valossa515/dotcourier?style=flat&logo=github" alt="Estrelas no GitHub"/></a>
  <a href="https://github.com/Valossa515/dotcourier/commits/main"><img src="https://img.shields.io/github/last-commit/Valossa515/dotcourier" alt="Último commit"/></a>
</p>

---

> 🌐 **Language / Idioma:** 🇺🇸 [English](README.md) | 🇧🇷 **Português** (atual)

## 🧠 Por que DotCourier?

O MediatR 13+ passou a ter licença comercial. O DotCourier é uma alternativa **gratuita, licença
MIT**, com a mesma API: migrar é basicamente trocar o `using`. E ele já traz as peças que todo time
acaba escrevendo em volta do MediatR.

- 🔁 **Substituto direto do MediatR.** Mesmos nomes de contratos: `IRequest<T>`, `IRequestHandler<,>`,
  `INotification`, `IPipelineBehavior<,>`, `IMediator`, `ISender`, `IPublisher`. O `AddCourier`
  aceita os mesmos métodos de configuração do `AddMediatR`.
- 🛡️ **Pronto para produção.** Logging, validação, traces e métricas OpenTelemetry e timeouts já
  vêm embutidos. Retry, circuit breaker, rate limiting e bulkhead estão num pacote extra.
- 🧾 **Erro vira resposta, não vazamento.** O `Dispatch` retorna um `Response<T>` com status code.
  As exceções são mapeadas sem nunca colocar stack trace ou connection string no corpo HTTP.
- ☕ **Irmão do [Spring Courier](https://github.com/Valossa515/spring-courier).** Mesmo design na
  JVM: times com Java e .NET trabalham com um único modelo mental.

## 🔄 Antes / Depois

<p align="center">
  <img src="https://raw.githubusercontent.com/Valossa515/dotcourier/main/assets/before-after.svg" alt="Antes e depois de usar o DotCourier" width="100%"/>
</p>

<details>
<summary>Ver a mesma comparação como código</summary>

**Antes**: MediatR, license key, behaviors que você mantém e um `try/catch` por endpoint:

```csharp
builder.Services.AddMediatR(cfg =>
{
    cfg.LicenseKey = config["MediatR:Key"];
    cfg.RegisterServicesFromAssemblyContaining<Program>();
    cfg.AddOpenBehavior(typeof(MyValidation<,>));   // escrito à mão
    cfg.AddOpenBehavior(typeof(MyLogging<,>));      // escrito à mão
    cfg.AddOpenBehavior(typeof(MyPollyRetry<,>));   // escrito à mão
});

app.MapPost("/products", async (CreateProduct c, ISender s) =>
{
    try { return Results.Ok(await s.Send(c)); }
    catch (ValidationException e) { return Results.BadRequest(e.Errors); }
    catch (NotFoundException e) { return Results.NotFound(e.Message); }
    catch (Exception) { return Results.Problem(); }
});
```

**Depois**:

```csharp
builder.Services.AddCourier(cfg => cfg
    .RegisterServicesFromAssemblyContaining<Program>()
    .AddResilience(o => o.Retry.Enabled = true));

app.MapPost("/products", (CreateProduct c, ICourier courier) =>
    courier.Dispatch(c).ToResult());
```

</details>

## 📦 Pacotes

| Pacote | O que adiciona |
|---|---|
| [`DotCourier`](https://www.nuget.org/packages/DotCourier) | Dispatcher, pipeline, notificações, `Response<T>`, logging, validação, telemetria, timeouts |
| [`DotCourier.AspNetCore`](https://www.nuget.org/packages/DotCourier.AspNetCore) | `Response<T>.ToResult()` (Minimal APIs) e `ToActionResult()` (MVC) |
| [`DotCourier.Resilience`](https://www.nuget.org/packages/DotCourier.Resilience) | Retry, circuit breaker, rate limiter e bulkhead sobre Polly v8 |
| [`DotCourier.FluentValidation`](https://www.nuget.org/packages/DotCourier.FluentValidation) | Roda seus `IValidator<T>` no pipeline; exceções do FluentValidation viram 400 |
| [`DotCourier.Caching`](https://www.nuget.org/packages/DotCourier.Caching) | Cache de queries, commands idempotentes e invalidação de cache sobre HybridCache (pronto para Redis) |

Todos os pacotes têm como alvo **net8.0** e **net10.0**.

## 🧪 Aplicação de exemplo

O [**dotcourier-store**](https://github.com/Valossa515/dotcourier-store) é uma loja (catálogo,
pedidos, pagamento, relatório) em **arquitetura hexagonal com DDD** que usa todas as funcionalidades
abaixo com chamadas HTTP reais: validação, códigos de erro, cache, idempotência, retry, circuit
breaker, rate limit, bulkhead, timeouts, notificações e mensagens traduzidas. Tem Swagger UI, EF Core
com SQLite e um teste de ponta a ponta que o CI roda a cada push.

## ⚡ Começo rápido

```bash
dotnet add package DotCourier
dotnet add package DotCourier.AspNetCore
```

```csharp
builder.Services.AddCourier(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
```

```csharp
public sealed record CreateProduct([Required] string Name, [Range(0.01, 10_000)] decimal Price) : ICommand<Guid>;

public sealed class CreateProductHandler(AppDbContext db) : ICommandHandler<CreateProduct, Guid>
{
    public async Task<Guid> Handle(CreateProduct command, CancellationToken ct)
    {
        var product = new Product(command.Name, command.Price);
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return product.Id;
    }
}

public sealed record GetProduct(Guid Id) : IQuery<Response<ProductDto>>;

public sealed class GetProductHandler(AppDbContext db) : IQueryHandler<GetProduct, Response<ProductDto>>
{
    public async Task<Response<ProductDto>> Handle(GetProduct query, CancellationToken ct) =>
        await db.Products.FindAsync([query.Id], ct) is { } p
            ? new ProductDto(p.Id, p.Name, p.Price)          // sucesso implícito
            : Response.NotFound($"Product {query.Id} not found");
}
```

```csharp
app.MapPost("/products", (CreateProduct cmd, ICourier courier, CancellationToken ct) =>
    courier.Dispatch(cmd, ct).ToResult());

app.MapGet("/products/{id:guid}", (Guid id, ICourier courier, CancellationToken ct) =>
    courier.Dispatch(new GetProduct(id), ct).ToResult());
```

Um `CreateProduct` inválido nem chega ao handler e volta assim:

```json
{ "data": null, "error": "Validation failed with 1 error(s)", "success": false, "statusCode": 400,
  "errorDetails": [ { "field": "Price", "message": "The field Price must be between 0.01 and 10000." } ] }
```

## 📨 `Send` vs `Dispatch`

| | Retorna | Em caso de falha |
|---|---|---|
| `Send(request)` | `TResponse` | lança exceção (semântica do MediatR) |
| `Dispatch(request)` | `Response<TResponse>` | retorna uma resposta de erro; só relança o cancelamento que você pediu |

O `Dispatch` só coloca na resposta a mensagem de `CourierException` (e subclasses). Qualquer outra
exceção vira `"An internal error occurred"` com status 500. Para expor erros de negócio, lance
subclasses de `CourierException`:

```csharp
public sealed class OutOfStockException(string sku) : CourierException($"SKU {sku} is out of stock", 409);
```

Se o handler já retorna `Response<T>`, o `Dispatch` devolve como está, sem embrulhar duas vezes.

### Atalhos de status e códigos de erro

Handlers que retornam `Response<T>` escolhem o status com atalhos. Sem classe base, sem `try/catch`:

```csharp
public sealed class ShipOrderHandler(AppDbContext db) : ICommandHandler<ShipOrder, Response<ShipmentDto>>
{
    public async Task<Response<ShipmentDto>> Handle(ShipOrder cmd, CancellationToken ct)
    {
        var order = await db.Orders.FindAsync([cmd.OrderId], ct);
        if (order is null) return Response.NotFound("Order not found", code: "ORD-404");
        if (order.OwnerId != cmd.UserId) return Response.Forbidden("Not your order");
        if (order.Shipped) return Response.NoContent();

        var shipment = order.Ship();
        await db.SaveChangesAsync(ct);
        return Response.Created(shipment.ToDto());
    }
}
```

| Atalho | Status |
|---|---|
| retornar o valor / `Response.Success(x)` | 200 |
| `Response.Created(x)` | 201 |
| `Response.NoContent()` | 204 (o `ToResult()` não envia corpo) |
| `Response.BadRequest(msg)` | 400 |
| `Response.Unauthorized(msg)` | 401 |
| `Response.Forbidden(msg)` | 403 |
| `Response.NotFound(msg)` | 404 |
| `Response.Conflict(msg)` | 409 |
| `Response.Failure(msg, status)` | qualquer um |

Todo atalho de erro aceita um `code` opcional, serializado como `"errorCode"` (omitido quando
ausente), para o cliente decidir o que fazer. Exceções também têm código:
`throw new CourierException("Out of stock", 409) { Code = "STK-001" };`
Erros de validação têm `code` por campo (o `WithErrorCode` do FluentValidation é mantido).

### Mensagens de erro e tradução

O DotCourier escreve algumas mensagens por conta própria: o 500 genérico, o resumo da validação e
as rejeições de timeout e de resiliência. Todas ficam em `cfg.Messages`, então você pode
traduzi-las e dar códigos a elas:

```csharp
builder.Services.AddCourier(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();

    // texto fixo, código opcional
    cfg.Messages.UnexpectedError = new("Ocorreu um erro inesperado", code: "000");

    // avaliada a cada erro, então segue o idioma de cada request (.resx)
    cfg.Messages.ValidationFailed = CourierMessage.From(() => Resources.ValidationFailed, code: "VAL-000");

    // os placeholders são preenchidos
    cfg.Messages.RequestTimeout = "{request} demorou mais de {timeoutMs} ms";
});
```

| Mensagem | Status | Padrão | Placeholders |
|---|---|---|---|
| `UnexpectedError` | 500 | An internal error occurred | |
| `ValidationFailed` | 400 | Validation failed with {count} error(s) | `{count}`, `{request}` |
| `HandlerNotFound` | 500 | No handler registered for request type '{request}'. | `{request}` |
| `RequestTimeout` | 504 | Request '{request}' timed out after {timeoutMs} ms | `{request}`, `{timeoutMs}` |
| `CircuitOpen` / `BulkheadRejected` | 503 | Service temporarily unavailable / Service busy... | `{request}` |
| `RateLimitExceeded` | 429 | Too many requests... | `{request}` |

O `CourierMessage.From(() => ...)` roda a cada erro, no fluxo do próprio request. Com a localização
por request do ASP.NET Core, o mesmo `.resx` responde em português ou inglês conforme o request.
Mensagens que você mesmo escreve (`Response.NotFound("...")`, `CourierException`) não mudam.

## 🧩 Pipeline

```csharp
builder.Services.AddCourier(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();
    cfg.AddOpenBehavior(typeof(AuditBehavior<,>));                               // ordem 0: o mais interno
    cfg.AddOpenBehavior(typeof(TenantBehavior<,>), order: BehaviorOrder.Logging + 1);
    cfg.AddBehavior<CreateProductMetricsBehavior>();                             // fechado: só um request
});
```

A posição de cada behavior vem de uma **ordem** explícita, não da sequência em que foi registrado.
Menor = mais externo:

| Ordem | Behavior | Pacote |
|------:|---|---|
| -2000 | exception handlers (`IRequestExceptionHandler<,,>`) | core |
| -1900 / -1800 | pré / pós-processadores | core |
| -1000 | logging | core |
| -995 | idempotência | Caching |
| -990 | telemetria | core |
| -951 / -950 | invalidação de cache / cache | Caching |
| -900 | validação | core |
| -870 | rate limiter | Resilience |
| -860 | circuit breaker | Resilience |
| -850 | retry | Resilience |
| -840 | bulkhead | Resilience |
| -700 | timeout | core |
| 0 | seus behaviors (padrão) | |

Desligue os embutidos com `cfg.EnableLogging`, `EnableTelemetry`, `EnableValidation`,
`UseDataAnnotations` ou `EnableTimeouts`.

## ✅ Validação

DataAnnotations funcionam como escritas em records posicionais. O `Validator` padrão não enxerga
atributos nos parâmetros do record; o DotCourier enxerga:

```csharp
public sealed record Register([Required] string Name, [EmailAddress] string Email) : ICommand;
```

Para regras que precisam de serviços, implemente `IRequestValidator<T>` (registrado pelo scan de assemblies):

```csharp
public sealed class RegisterValidator(AppDbContext db) : IRequestValidator<Register>
{
    public async ValueTask<IEnumerable<ValidationError>> ValidateAsync(Register r, CancellationToken ct) =>
        await db.Users.AnyAsync(u => u.Email == r.Email, ct)
            ? [new ValidationError(nameof(r.Email), "Email already registered")]
            : [];
}
```

### FluentValidation

```bash
dotnet add package DotCourier.FluentValidation
```

```csharp
builder.Services.AddCourier(cfg => cfg
    .RegisterServicesFromAssemblyContaining<Program>()
    .AddFluentValidation());

public sealed class SignupValidator : AbstractValidator<Signup>
{
    public SignupValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress().WithErrorCode("EMAIL-01");
}
```

- Seus `IValidator<T>` rodam no passo de validação, antes do handler, e as falhas se juntam às do
  DataAnnotations num **único 400**. Os validators dos assemblies escaneados são registrados para
  você (passe `registerValidators: false` se já usa `AddValidatorsFromAssembly`).
- Uma `ValidationException` do FluentValidation lançada **dentro** do handler (por exemplo com
  `ValidateAndThrowAsync`) também vira 400. Sem este pacote ela é uma exceção inesperada e o
  `Dispatch` responde 500. A conversão acontece colada ao handler, então o retry nunca repete um
  request inválido.

## 🔌 A sua própria interface de handler

Já tem um `IHandler<TInput, TOutput>` com um método `Execute` (muitas vezes junto de um
`HandlerBase` que faz validação e `try/catch`)? Mantenha. Faça a interface herdar de
`IRequestHandler` uma vez, com um método padrão (default interface method) que encaminha o
`Handle` para o `Execute`, e todos os handlers existentes são descobertos e passam pelo pipeline
sem mudar nada:

```csharp
public interface IHandler<TInput, TOutput> : IRequestHandler<TInput, Response<TOutput>>
    where TInput : IRequest<Response<TOutput>>
{
    Task<Response<TOutput>> Execute(TInput request, CancellationToken ct);

    // A ponte com o DotCourier, escrita uma vez. Os handlers nunca a veem.
    Task<Response<TOutput>> IRequestHandler<TInput, Response<TOutput>>.Handle(TInput request, CancellationToken ct)
        => Execute(request, ct);
}

public sealed record GetOrder(int Id) : IQuery<Response<OrderDto>>;

public sealed class GetOrderHandler(AppDbContext db) : IHandler<GetOrder, OrderDto>
{
    public async Task<Response<OrderDto>> Execute(GetOrder request, CancellationToken ct) =>
        await db.Orders.FindAsync([request.Id], ct) is { } order
            ? order.ToDto()
            : Response.NotFound($"Order {request.Id} not found", code: "ORD-404");
}
```

O `HandlerBase` pode sair: validação, mapeamento de erros e log vêm do pipeline, e os atalhos
`NotFound`/`Created`/... dele viram `Response.NotFound(...)` e companhia.

> O Spring Courier encontra esses handlers pelo nome do método (`handle`/`execute`) em tempo de
> execução. No .NET a ponte é verificada pelo compilador e não usa reflection a cada chamada.

## 🧬 Requests e handlers genéricos

Tudo o que o Spring Courier suporta funciona, e o DotCourier vai além:

```csharp
// Um único handler genérico atende Create<Product>, Create<Order>, ...
public sealed record Create<T>(T Entity) : ICommand<T>;

public sealed class CreateHandler<T>(AppDbContext db) : ICommandHandler<Create<T>, T>
    where T : class, IEntity
{
    public async Task<T> Handle(Create<T> cmd, CancellationToken ct)
    {
        db.Set<T>().Add(cmd.Entity);
        await db.SaveChangesAsync(ct);
        return cmd.Entity;
    }
}

// Roda para toda notificação que implementa IAudited.
public sealed class AuditNotifications<T>(IAuditLog audit) : INotificationHandler<T>
    where T : INotification, IAudited
{
    public Task Handle(T notification, CancellationToken ct) => audit.Write(notification, ct);
}
```

| Formato | Spring Courier | DotCourier |
|---|---|---|
| Handler que herda de uma base genérica (`Handler : BaseHandler<Cmd, Result>`) | ✅ | ✅ |
| Handler por interface própria (`IMyHandler<T> : IRequestHandler<T, X>`) | ✅ | ✅ |
| Request genérico com um handler aberto (`CreateHandler<T>` para `Create<T>`) | ❌ | ✅ |
| Handler de notificação / processador / validador aberto (`X<T> : INotificationHandler<T>`) | ❌ | ✅ |

Handlers de request abertos são fechados no **primeiro dispatch** de cada tipo de request: os
argumentos de tipo são deduzidos do request, as constraints são verificadas (um request que não
as satisfaz recebe `HandlerNotFoundException`) e a fábrica de criação fica em cache. Não há custo
na inicialização nem limite de quantos tipos fechados você usa. Se dois handlers abertos servirem
para o mesmo request, o DotCourier lança erro em vez de escolher um. Logs, traces e métricas
chamam esses requests de `Create<Product>`, não ``Create`1``.

## 🛡️ Resiliência

```bash
dotnet add package DotCourier.Resilience
```

```csharp
builder.Services.AddCourier(cfg => cfg
    .RegisterServicesFromAssemblyContaining<Program>()
    .AddResilience(o =>
    {
        o.Retry.Enabled = true;                         // opt-in
        o.ForRequest<GenerateReport>(r =>
        {
            r.Bulkhead.Enabled = true;
            r.Bulkhead.MaxConcurrency = 2;
        });
        o.ForRequest<PlaceOrder>(r => r.Retry.Enabled = false); // não é idempotente
    }));
```

Cada tipo de request tem o **seu próprio** circuito, janela de rate limit e pool de bulkhead.

| Estratégia | Padrão | Na rejeição |
|---|---|---|
| Circuit breaker | **ligado** | `CircuitOpenException` → **503** |
| Retry | desligado | (a última falha é propagada) |
| Rate limiter | desligado | `RateLimitExceededException` → **429** |
| Bulkhead | desligado | `BulkheadRejectedException` → **503** |

**Por que só o breaker vem ligado:** ele só age depois que algo já está falhando, então não tem como
estrangular um sistema saudável. Retry pode aplicar duas vezes um command não idempotente, e rate
limiter e bulkhead impõem tetos rígidos; os três são escolhas conscientes.

A posição no pipeline também é deliberada, e fixada por testes:

- **Breaker por fora do retry.** Com o circuito aberto, a chamada é rejeitada *uma vez* em vez de
  ser repetida N vezes. Retry contra dependência caída amplifica o incidente.
- **Bulkhead por dentro do retry.** A vaga é tomada por tentativa e liberada durante o backoff,
  então retries dormindo não esfomeiam o pool.
- **Rate limiter depois da validação.** Request inválido não consome quota.

Erros do cliente (`CourierException` com status abaixo de 500: validação, não encontrado,
conflito) não são repetidos nem contados pelo breaker. Mude isso com `o.ShouldHandle`.
Configuração inválida falha na inicialização, não no primeiro request.

## 🗄️ Cache e idempotência

```bash
dotnet add package DotCourier.Caching
```

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379"); // opcional: camada compartilhada
builder.Services.AddCourier(cfg => cfg
    .RegisterServicesFromAssemblyContaining<Program>()
    .AddCaching());

[Cacheable(60)]                                   // segundos; omita para o padrão (5 min)
public sealed record GetProduct(Guid Id) : IQuery<Response<ProductDto>>;

[InvalidatesCache(typeof(GetProduct), typeof(ListProducts))]
public sealed record UpdatePrice(Guid Id, decimal Price) : ICommand<Response<Unit>>;

[Idempotent]                                      // padrão: lembrado por 1 hora
public sealed record PlaceOrder(string IdempotencyKey, Cart Cart) : ICommand<Response<OrderDto>>, IIdempotencyKey;
```

- **`[Cacheable]`** responde requests iguais a partir do cache. Só resultados de sucesso e não
  nulos são guardados; respostas de erro e exceções nunca.
- **`[InvalidatesCache]`** remove do cache as queries listadas quando o command dá certo (depois
  da transação, porque fica por fora dela). `typeof(GetById<>)` remove todos os tipos fechados.
- **`[Idempotent]`** executa um command uma vez só: repetições recebem o resultado registrado, e
  **duplicatas simultâneas esperam a primeira execução** em vez de rodarem em paralelo. Falhas
  não são registradas, então uma nova tentativa executa de novo. Implemente `IIdempotencyKey`
  para usar como chave um header `Idempotency-Key` enviado pelo cliente, em vez do conteúdo do request.
- **`ICourierCache`** remove por código: `InvalidateAsync<GetProduct>()`, `InvalidateAsync(request)`,
  `InvalidateAllAsync()`, `ForgetIdempotentResultAsync(request)`.

O armazenamento é o **HybridCache** da Microsoft: memória em cada instância, mais qualquer
`IDistributedCache` registrado (Redis, SQL Server, ...) como camada compartilhada, então um
resultado calculado numa instância é reaproveitado pelas outras. O `AddDistributedMemoryCache()`
**não** conta como camada compartilhada (o HybridCache o ignora, já que ele também fica em memória).

A chave vem do request **serializado em JSON** (com hash), não do `ToString()`: o `ToString()` de
um record imprime o tipo de uma lista, não os itens, e dois requests diferentes dividiriam a mesma
entrada. Implemente `ICacheKey` para escolher a chave você mesmo. Os resultados guardados precisam
ser serializáveis com System.Text.Json.

| Opção (`AddCaching(o => ...)`) | Padrão | |
|---|---|---|
| `DefaultCacheDuration` | 5 min | para `[Cacheable]` sem segundos |
| `DefaultIdempotencyDuration` | 1 h | para `[Idempotent]` sem segundos |
| `CacheAllQueries` | `false` | guarda toda `IQuery<T>` (comportamento do Spring Courier) |
| `LocalCacheDuration` | duração da entrada | limita a cópia em memória; com várias instâncias, a remoção limpa a camada compartilhada, mas as outras instâncias mantêm a cópia em memória até isso expirar |
| `KeyPrefix` | `dotcourier` | para dividir um servidor de cache entre aplicações |

## ⏱️ Timeouts

```csharp
[HandlerTimeout(2_000)]
public sealed record GenerateReport(int Year) : IQuery<Report>;
```

O `CancellationToken` do handler é cancelado após 2 s, e quem chamou recebe uma
`RequestTimeoutException` (504) no tempo certo mesmo que o handler ignore o token. Roda no mesmo
fluxo assíncrono, então combina com transações e contexto ambiente. Com retry ligado, cada
tentativa tem seu próprio timeout.

## 📣 Notificações

```csharp
await courier.Publish(new ProductCreated(id), ct);
```

| `cfg.NotificationPublishStrategy` | Comportamento |
|---|---|
| `Sequential` (padrão) | um por vez; falhas são logadas, os demais continuam, `Publish` não lança |
| `ParallelWhenAll` | todos de uma vez; falhas logadas; evite compartilhar um `DbContext` scoped |
| `StopOnFirstError` | um por vez; a primeira falha lança `NotificationHandlerException` |

> O publisher padrão do MediatR lança na primeira falha. Se você depende disso, use `StopOnFirstError`.

## 📈 Telemetria

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(CourierDiagnostics.SourceName))
    .WithMetrics(m => m.AddMeter(CourierDiagnostics.SourceName));
```

| Sinal | Nome | Tags |
|---|---|---|
| Activity | `courier {RequestType}` | `courier.request.type`, `error.type` |
| Counter | `courier.requests` | `courier.request.type`, `courier.outcome`, `error.type` |
| Histogram (s) | `courier.request.duration` | as mesmas acima |
| Counter | `courier.resilience.rejected` | `courier.request.type`, `courier.resilience.strategy` |
| Counter | `courier.retry.attempts` | `courier.request.type` |
| Counter | `courier.cache.hits` / `courier.cache.misses` | `courier.request.type` |
| Counter | `courier.idempotency.hits` / `courier.idempotency.misses` | `courier.request.type` |

## 🔀 Migrando do MediatR

1. Troque o pacote `MediatR` por `DotCourier`, e `using MediatR;` por `using DotCourier;`.
2. Troque `services.AddMediatR(cfg => ...)` por `services.AddCourier(cfg => ...)`.
   `RegisterServicesFromAssembly*`, `AddBehavior` e `AddOpenBehavior` mantêm os nomes.
3. `IMediator`, `ISender` e `IPublisher` continuam sendo resolvidos. Injete `ICourier` onde quiser usar o `Dispatch`.
4. Usa FluentValidation com um `ValidationBehavior` escrito à mão? Apague-o, adicione o
   `DotCourier.FluentValidation` e chame `cfg.AddFluentValidation()`.

Diferenças para ficar de olho:

- `IRequest` é `IRequest<Unit>`, como no MediatR 11, então `Send` num request sem retorno devolve `Task<Unit>`.
- Por padrão, falhas em handlers de notificação são logadas em vez de lançadas (veja acima).
- Logging, telemetria, validação e timeouts vêm ligados por padrão.
- Requests de streaming (`IStreamRequest`) ainda não são suportados.

### Comparação rápida

| | MediatR 13+ | DotCourier |
|---|---|---|
| Licença | comercial (license key) | **MIT** |
| API de request / handler / behavior | ✅ | ✅ mesmos nomes |
| Validação, logging e telemetria embutidos | ❌ você escreve | ✅ |
| FluentValidation no pipeline | ❌ você escreve o behavior | ✅ `DotCourier.FluentValidation` |
| Handlers de request genéricos (`Create<T>`) | opt-in, fechados na inicialização | ✅ fechados no primeiro dispatch |
| Retry / circuit breaker / rate limit / bulkhead | ❌ | ✅ `DotCourier.Resilience` |
| Cache de queries / commands idempotentes | ❌ | ✅ `DotCourier.Caching` |
| Exceções → `Response<T>` com status code | ❌ | ✅ `Dispatch` |
| Ordem explícita de behaviors | ordem de registro | ✅ `BehaviorOrder` |
| Requests de streaming | ✅ | 🔜 |

## 🗺️ Roadmap

Cada módulo do [Spring Courier](https://github.com/Valossa515/spring-courier) ganha um equivalente em .NET:

- [x] Core: dispatcher, pipeline, validação, telemetria, timeouts
- [x] Integração com ASP.NET Core
- [x] Resiliência: retry, circuit breaker, rate limiter, bulkhead (Polly v8)
- [x] Integração com FluentValidation
- [x] Requests genéricos e handlers genéricos abertos
- [x] Atalhos de status e códigos de erro
- [x] Cache e idempotência: `HybridCache` / `IDistributedCache` (Redis incluso)
- [ ] Transações: behavior para EF Core / `TransactionScope`
- [ ] Transactional outbox com EF Core
- [ ] Entrega do outbox para Kafka
- [ ] Requests de streaming
- [ ] Source generator para dispatch sem reflection e compatível com AOT

## 🤝 Contribuindo

Contribuições são bem-vindas. Leia o [guia de contribuição](CONTRIBUTING.pt-BR.md) e o
[código de conduta](CODE_OF_CONDUCT.pt-BR.md). Para reportar uma vulnerabilidade, siga a
[política de segurança](SECURITY.pt-BR.md); por favor, não abra uma issue pública.

## 📄 Licença

[MIT](LICENSE). Livre para uso comercial, para sempre.
