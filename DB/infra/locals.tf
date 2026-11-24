locals {
  common_tags = {
    Project     = var.project_name
    Environment = var.environment
    ManagedBy   = "terraform"
  }

  # Prefixo dos recursos deste repo (RDS, subnet group, SG e tabela DynamoDB do Execucao e Producao),
  # para nao colidir com os do DB/infra dos repos MANAGEMENT, OS-SERVICE e BILLING. As buscas da rede
  # continuam por var.project_name.
  name_prefix = "${var.project_name}-execution"
}
