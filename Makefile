# Kindred - the whole thing in Docker: Postgres, RabbitMQ, Neo4j, both services, the gateway
# and the frontend. Only Docker and make are needed to run it.
#
#   make kindred-start          build the images and start everything; returns when it is healthy
#   make kindred-stop           stop everything, keep the data
#   make kindred-stop WIPE=1    also delete the databases, the queues and the knowledge graph
#
# To run the app on the host instead (hot reload): scripts/start-dev.ps1 and scripts/stop-dev.ps1.

COMPOSE := docker compose -f docker-compose.dev.yml --profile kg --profile app

.PHONY: help kindred-start kindred-stop

help:
	@echo make kindred-start          build and start everything in Docker
	@echo make kindred-stop           stop everything, keep the data
	@echo make kindred-stop WIPE=1    stop everything and delete all data

kindred-start:
	@$(COMPOSE) up -d --build --wait
	@echo Kindred is running.
	@echo   App        http://localhost:11200
	@echo   Gateway    http://localhost:11000/api/v1/courses
	@echo   RabbitMQ   http://localhost:11673   kindred / kindred_dev
	@echo   Neo4j      http://localhost:11474   neo4j / kindred_kg_dev

kindred-stop:
	@$(COMPOSE) down $(if $(WIPE),-v)
