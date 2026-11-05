# Sistema de Gestão

Sistema interno de uma transportadora: fretes, viagens dos caminhões, romaneios e e-mails aos clientes,
estoques, abastecimentos, financeiro e fechamento do mês. Tem acesso de administrador e painel do motorista.


## Stack

| Camada | Tecnologia |
|---|---|
| Frontend | React 18 + Vite 5 + TypeScript + Tailwind, react-router, PWA (`frontend/`) |
| API | ASP.NET Core 8, JWT, Swagger (`src/TransportadoraSystem.API`) |
| Regras de negócio | `src/TransportadoraSystem.Application/Services`: leitura de XML, PDF e planilhas, cálculos, Excel, e-mail |
| Dados | EF Core 8 + Npgsql sobre PostgreSQL (Supabase); tabelas novas pelo `Ddl.cs` ao subir a API |
| Arquivos | ClosedXML (gera .xlsx), ExcelDataReader (lê .xls/.xlsx), PdfPig (lê PDF), Tesseract (OCR da CNH) |
| Hospedagem | site na Cloudflare Pages, API na Render (Docker), banco no Supabase; push na `main` publica |

## Mapa da arquitetura

```mermaid
flowchart TB
    subgraph Usuarios[Quem usa]
        ADM[Administrador]
        MOT[Motorista: só o próprio painel]
    end
    subgraph Front[Frontend: Cloudflare Pages]
        PWA[React + Vite, PWA instalável]
        AUTH[AuthContext: sessão e rotas por perfil]
    end
    subgraph API[API: Render, Docker]
        CTRL[37 controllers REST com JWT]
        SRV[Services: regras de negócio]
        LEIT[Leitores: XML de NF-e e CT-e, PDF, planilhas, OCR da CNH]
        GER[Geradores: planilhas .xlsx a partir de modelos]
        DDL[Ddl.cs: cria tabelas e colunas ao subir]
    end
    subgraph Dados[Supabase]
        PG[(PostgreSQL)]
    end
    subgraph Externo[Serviços externos]
        BREVO[Brevo: envio de e-mail por API HTTP]
    end
    ADM --> PWA
    MOT --> PWA
    PWA --> AUTH
    AUTH -->|axios + token| CTRL
    CTRL --> SRV
    SRV --> LEIT
    SRV --> GER
    SRV -->|EF Core| PG
    DDL --> PG
    SRV --> BREVO
```

## Mapa dos módulos

```mermaid
flowchart LR
    subgraph Dia[Dia a dia]
        D1[Viagens e cálculo do frete]
        D2[Viagens dos caminhões e retornos]
        D3[Romaneios]
        D4[Abastecimentos e cobranças]
        D5[E-mails aos clientes]
    end
    subgraph Emb[Embalados]
        E1[Estoque de volumes embalados]
        E2[Quebras, despesas, comissões e salários]
    end
    subgraph Est[Estoques]
        S1[ProdutosA, produtosB e produtosC]
    end
    subgraph Bar[Barracão]
        B1[Comissões e despesas]
    end
    subgraph Fin[Financeiro]
        F1[Boletos]
        F2[Acerto com a fazenda]
        F3[Despesas divididas e saldos]
    end
    subgraph Mes[Fechamento do mês]
        M1[Planilha do seguro a partir dos CT-e]
        M2[Controle do pedágio eletrônico]
    end
    subgraph Cad[Cadastros]
        C1[Motoristas, placas, preços, tipos de volume, notas]
    end
    Cad --> Dia
    Cad --> Emb
    D1 --> D3
    D3 --> D5
    F1 --> D5
    D4 --> F1
    D2 --> F3
```

## Fluxos principais

```mermaid
flowchart TD
    subgraph Frete[Frete e romaneio]
        A1[XML das NF-e] --> A2[Importação agrupa por destinatário]
        A2 --> A3[Cálculo do frete na tela Viagens]
        A2 --> A4[Romaneio por cliente em .xlsx]
        A4 --> A5[Boletos do cliente anexados]
        A5 --> A6[E-mail semanal pelo Brevo]
    end
    subgraph Abast[Abastecimento]
        B1[Fatura PDF + planilha + boleto do posto] --> B2[Conferência cruzada]
        B2 --> B3[Divisão por posto e resumo a pagar]
    end
    subgraph Viagem[Viagem do caminhão]
        C1[Motorista preenche a folha no painel] --> C2[Acerto: diárias, comissão, sobra em cadeia]
        C2 --> C3[Retornos]
    end
    subgraph Fecha[Fechamento do mês]
        D1[XML dos CT-e] --> D2[Planilha do seguro]
        D3[Planilha do pedágio] --> D4[Controle mensal]
        D2 --> D5[.xlsx exportado]
        D4 --> D5
    end
    subgraph Fazenda[Acerto com a fazenda]
        E1[Upload único reconhece cada arquivo] --> E2[Semana montada com saldo anterior automático]
        E2 --> E3[Planilha do acerto]
    end
```

Não há tarefas agendadas: tudo é disparado pelo usuário. Integração bancária para emitir boletos e emissão de
CT-e e MDF-e por API estão planejadas.

## Rodar e conferir

```bash
dotnet build
```
```bash
cd frontend && npm install && npm run dev
```

Antes de subir: `dotnet build` e `npm run build`, depois teste com arquivos reais (XML, PDF, planilhas).
