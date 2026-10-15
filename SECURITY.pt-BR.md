> 🌐 **Language / Idioma:** 🇺🇸 [English](SECURITY.md) | 🇧🇷 **Português** (atual)

# 🔒 Política de Segurança

## Versões suportadas

O DotCourier ainda está antes da 1.0: só a minor mais recente recebe correções.

| Versão | Suportada |
| ------ | --------- |
| 0.8.x  | ✅ Sim    |
| < 0.8  | ❌ Não    |

## Reportando uma vulnerabilidade

**⚠️ Não abra uma issue pública para vulnerabilidades de segurança.**

Reporte de forma privada, por um destes canais:

1. **[GitHub Security Advisories](https://github.com/Valossa515/dotcourier/security/advisories/new)** (preferível)
2. E-mail para **fe.mmo515@gmail.com**

### O que incluir

- Descrição da vulnerabilidade e do impacto potencial
- Passos para reproduzir, de preferência com um projeto mínimo
- Pacote(s) e versão(ões) afetados
- Sugestão de correção, se tiver

### O que esperar

- **Confirmação de recebimento** em até 48 horas
- **Avaliação inicial** em até 7 dias
- **Correção e release** de acordo com a severidade:
  - 🔴 **Crítica**: o quanto antes
  - 🟠 **Alta**: no próximo patch
  - 🟡 **Média**: na próxima minor
  - 🟢 **Baixa**: numa release futura

### Divulgação responsável

Dê um prazo razoável para publicarmos a correção antes de divulgar, não explore a falha além do
necessário para demonstrá-la e não acesse nem modifique dados de outras pessoas. Quem reportar
problemas válidos recebe crédito (com permissão) nas release notes.

## Boas práticas de segurança ao usar o DotCourier

- Mantenha os pacotes atualizados.
- Prefira `ICourier.Dispatch` nas bordas HTTP: só mensagens de `CourierException` chegam à
  resposta, então stack traces, SQL e connection strings nunca vazam.
- Só lance subclasses de `CourierException` com mensagens que você aceita mostrar a quem chamou.
- Valide os requests com DataAnnotations ou `IRequestValidator<T>`; o behavior de validação roda
  antes do seu handler.
- O behavior de logging registra o **nome do tipo** do request, nunca o conteúdo. Mantenha isso nos
  seus próprios behaviors: requests costumam carregar dados pessoais ou segredos.
- Ligue o rate limiting do `DotCourier.Resilience` em operações caras ou passíveis de abuso.
