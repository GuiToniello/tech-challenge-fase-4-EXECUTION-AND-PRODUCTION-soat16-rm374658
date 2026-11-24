# Historico da producao do Execucao e Producao em NoSQL (DynamoDB), complementar ao RDS.
# O PostgreSQL (rds.tf) continua sendo a fonte da verdade: a API grava aqui em best-effort, e uma
# falha no DynamoDB nao quebra o fluxo da producao. Os itens sao descartaveis e expiram pelo TTL
# (ExpiraEm, epoch em segundos preenchido pela API, 180 dias apos o evento).
# Chave: OrdemServicoId (particao) + OcorridoEm (ordenacao, data/hora em texto), para consultar a
# linha do tempo da producao de uma OS em ordem cronologica.
# Sob demanda, sem PITR e sem streams; deletion_protection desligado para o Destroy funcionar.
# As tags vem do default_tags do provider.
resource "aws_dynamodb_table" "historico_producao" {
  name                        = "${local.name_prefix}-producao-historico"
  billing_mode                = "PAY_PER_REQUEST"
  hash_key                    = "OrdemServicoId"
  range_key                   = "OcorridoEm"
  deletion_protection_enabled = false

  attribute {
    name = "OrdemServicoId"
    type = "S"
  }

  attribute {
    name = "OcorridoEm"
    type = "S"
  }

  ttl {
    attribute_name = "ExpiraEm"
    enabled        = true
  }
}
