> 🌐 **Language / Idioma:** 🇺🇸 [English](CONTRIBUTING.md) | 🇧🇷 **Português** (atual)

# 🤝 Contribuindo com o DotCourier

Obrigado por considerar contribuir! Reports de bug, correções na documentação, testes e
funcionalidades são todos bem-vindos. Ao participar, você concorda com o
[Código de Conduta](CODE_OF_CONDUCT.pt-BR.md).

## 🚀 Como contribuir

### 1. Fork e clone

```bash
git clone https://github.com/<seu-usuario>/dotcourier.git
cd dotcourier
```

Você precisa do **SDK do .NET 10** e do **runtime do .NET 8** (os testes rodam nos dois target frameworks).

### 2. Crie uma branch

```bash
git checkout -b feat/minha-feature
```

### 3. Build e testes

```bash
dotnet build
dotnet test
```

O build trata **warnings como erros**, e todo membro público dos projetos em `src/` precisa de um
`<summary>` XML. Se compilou, a documentação está completa.

### 4. Commit

Usamos [Conventional Commits](https://www.conventionalcommits.org/pt-br/):

```bash
git commit -m "feat(resilience): add per-request timeout to retry"
git commit -m "fix: keep behavior order stable for equal orders"
git commit -m "docs: clarify Dispatch error mapping"
```

### 5. Abra um pull request

Descreva o problema e a solução, linke a issue e preencha o checklist do PR.

## 🧭 Regras de design

Elas mantêm o DotCourier previsível; PRs que as quebrem vão receber pedido de ajuste.

1. **API compatível com o MediatR.** `IRequest<T>`, `IRequestHandler<,>`, `INotification`,
   `IPipelineBehavior<,>`, `IMediator`/`ISender`/`IPublisher` e os nomes dos métodos de
   configuração do `AddCourier` são iguais aos do MediatR. Não renomeie.
2. **Sem vazamentos.** `Response<T>.FromException` só propaga mensagens de `CourierException`.
   Nunca coloque a mensagem de uma exceção qualquer numa resposta.
3. **Async + `CancellationToken` em tudo.** Nada de sync-over-async.
4. **O core não tem dependências** além de `Microsoft.Extensions.*.Abstractions`. Integrações
   (Polly, EF Core, Redis, Kafka) ficam em pacotes `DotCourier.*` próprios.
5. **A posição dos behaviors é explícita.** Behaviors embutidos novos ganham um lugar no
   `BehaviorOrder`, e toda decisão de ordem que importa tem um teste que falha se a ordem mudar.
6. **Nunca logue o conteúdo do request.** Só o nome do tipo.

## 🗂️ Estrutura do projeto

```
src/DotCourier/              pacote core
src/DotCourier.AspNetCore/   Response<T> → IResult / IActionResult
src/DotCourier.Resilience/   behaviors baseados em Polly
src/DotCourier.FluentValidation/  IValidator<T> no pipeline
src/DotCourier.Caching/      cache / idempotência sobre HybridCache
tests/DotCourier.Tests/      xUnit, roda em net8.0 e net10.0
assets/                      logo, ícone e imagens do README
```

### Adicionando um módulo

1. Crie `src/DotCourier.<Nome>/DotCourier.<Nome>.csproj` (multi-targeting e empacotamento vêm do
   `src/Directory.Build.props`).
2. Adicione as versões dos pacotes no `Directory.Packages.props` (gerenciamento central de pacotes).
3. Exponha uma extensão `cfg.Add<Nome>(...)` em `CourierConfiguration`. Registre behaviors com
   `AddOpenBehavior(tipo, BehaviorOrder.X)` e serviços com `ConfigureServices(...)`, para que tudo
   entre no registro ordenado único que o `AddCourier` faz.
4. Adicione o projeto ao `DotCourier.slnx` e os testes em `tests/DotCourier.Tests/<Nome>/`.
5. Documente nos dois READMEs e no `CLAUDE.md`.

## 📦 Processo de release (mantenedores)

As releases são publicadas no NuGet pelo `.github/workflows/publish-nuget.yml` usando
[NuGet Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): o
token OIDC do GitHub é trocado por uma API key de curta duração, então **nenhuma API key fica
guardada** no repositório.

Configuração única (já feita neste repositório):

- Uma política de trusted publishing no nuget.org para `Valossa515/dotcourier`, workflow
  `publish-nuget.yml`, pacotes `DotCourier*`.
- A variável de repositório `NUGET_USER` com o nome do perfil no nuget.org.

Para lançar:

1. Atualize a versão no `Directory.Build.props` (mantém os packs locais coerentes) e a tabela de
   versões suportadas no `SECURITY.md`.
2. Faça o merge na `main`.
3. Crie uma GitHub Release com a tag `vX.Y.Z`. O workflow roda os testes, empacota com essa versão
   e publica todos os pacotes.

> Publicar no NuGet é irreversível: versões podem ser despublicadas (unlist), mas nunca apagadas.

## 🐛 Reportando bugs e pedindo funcionalidades

Use os templates de issue. Para problemas de segurança, siga a
[política de segurança](SECURITY.pt-BR.md) em vez de abrir uma issue pública.
