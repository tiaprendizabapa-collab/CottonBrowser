-- PostgreSQL 15+. Execute em uma base dedicada ao monitoramento.
CREATE TABLE usuarios (
    id UUID PRIMARY KEY,
    identificador TEXT NOT NULL UNIQUE,
    nome TEXT NOT NULL,
    role TEXT NOT NULL CHECK (role IN ('Admin', 'User')),
    criado_em TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE sessoes (
    id UUID PRIMARY KEY,
    usuario_id UUID NOT NULL REFERENCES usuarios(id),
    inicio_em TIMESTAMPTZ NOT NULL,
    fim_em TIMESTAMPTZ,
    cliente TEXT NOT NULL DEFAULT 'CottonBrowser'
);

CREATE TABLE logs_navegacao (
    id BIGINT GENERATED ALWAYS AS IDENTITY,
    usuario_id UUID NOT NULL REFERENCES usuarios(id),
    sessao_id UUID NOT NULL REFERENCES sessoes(id),
    tipo TEXT NOT NULL CHECK (tipo IN ('navigation', 'active_tab')),
    url TEXT NOT NULL,
    dominio TEXT NOT NULL,
    termo_busca TEXT,
    titulo TEXT,
    aba_ativa BOOLEAN NOT NULL DEFAULT false,
    ocorrido_em TIMESTAMPTZ NOT NULL
) PARTITION BY RANGE (ocorrido_em);

CREATE TABLE logs_navegacao_2026_09 PARTITION OF logs_navegacao
    FOR VALUES FROM ('2026-09-01') TO ('2026-10-01');

CREATE INDEX logs_navegacao_usuario_data_idx ON logs_navegacao (usuario_id, ocorrido_em DESC);
CREATE INDEX logs_navegacao_dominio_idx ON logs_navegacao (dominio, ocorrido_em DESC);
CREATE INDEX logs_navegacao_busca_idx ON logs_navegacao (ocorrido_em DESC) WHERE termo_busca IS NOT NULL;

-- Ranking dos cinco domínios mais acessados:
-- SELECT dominio, COUNT(*) AS acessos FROM logs_navegacao
-- WHERE tipo = 'navigation' GROUP BY dominio ORDER BY acessos DESC LIMIT 5;

-- Últimas pesquisas:
-- SELECT ocorrido_em, usuario_id, dominio, termo_busca FROM logs_navegacao
-- WHERE termo_busca IS NOT NULL ORDER BY ocorrido_em DESC LIMIT 50;
