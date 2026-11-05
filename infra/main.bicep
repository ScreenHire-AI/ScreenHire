targetScope = 'subscription'

param resourceGroupName string

param location string

param principalId string

param postgres_admin_login string = 'screenhire_owner'

@secure()
param postgres_admin_password string

@secure()
param webhook_signing_key string

@secure()
param verification_device_hash_key string

@secure()
param email_reply_key string

@secure()
param sendgrid_inbound_secret string

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
}

module aca_acr 'aca-acr/aca-acr.bicep' = {
  name: 'aca-acr'
  scope: rg
  params: {
    location: location
  }
}

module aca 'aca/aca.bicep' = {
  name: 'aca'
  scope: rg
  params: {
    location: location
    aca_acr_outputs_name: aca_acr.outputs.name
    userPrincipalId: principalId
  }
}

module postgres_kv 'postgres-kv/postgres-kv.bicep' = {
  name: 'postgres-kv'
  scope: rg
  params: {
    location: location
  }
}

module postgres 'postgres/postgres.bicep' = {
  name: 'postgres'
  scope: rg
  params: {
    location: location
    administratorLogin: postgres_admin_login
    administratorLoginPassword: postgres_admin_password
    postgres_kv_outputs_name: postgres_kv.outputs.name
  }
}

module storage 'storage/storage.bicep' = {
  name: 'storage'
  scope: rg
  params: {
    location: location
  }
}

module vault 'vault/vault.bicep' = {
  name: 'vault'
  scope: rg
  params: {
    location: location
    webhook_signing_key_value: webhook_signing_key
    verification_device_hash_key_value: verification_device_hash_key
    email_reply_key_value: email_reply_key
    sendgrid_inbound_secret_value: sendgrid_inbound_secret
  }
}

module data_protection_key 'data-protection-key/data-protection-key.bicep' = {
  name: 'data-protection-key'
  scope: rg
  params: {
    location: location
    vaultName: vault.outputs.name
  }
}

module api_identity 'api-identity/api-identity.bicep' = {
  name: 'api-identity'
  scope: rg
  params: {
    location: location
  }
}

module api_roles_storage 'api-roles-storage/api-roles-storage.bicep' = {
  name: 'api-roles-storage'
  scope: rg
  params: {
    location: location
    storage_outputs_name: storage.outputs.name
    principalId: api_identity.outputs.principalId
  }
}

module api_roles_vault 'api-roles-vault/api-roles-vault.bicep' = {
  name: 'api-roles-vault'
  scope: rg
  params: {
    location: location
    vault_outputs_name: vault.outputs.name
    principalId: api_identity.outputs.principalId
  }
}

module worker_identity 'worker-identity/worker-identity.bicep' = {
  name: 'worker-identity'
  scope: rg
  params: {
    location: location
  }
}

module worker_roles_storage 'worker-roles-storage/worker-roles-storage.bicep' = {
  name: 'worker-roles-storage'
  scope: rg
  params: {
    location: location
    storage_outputs_name: storage.outputs.name
    principalId: worker_identity.outputs.principalId
  }
}

module worker_roles_vault 'worker-roles-vault/worker-roles-vault.bicep' = {
  name: 'worker-roles-vault'
  scope: rg
  params: {
    location: location
    vault_outputs_name: vault.outputs.name
    principalId: worker_identity.outputs.principalId
  }
}

output aca_AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN string = aca.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN

output aca_AZURE_CONTAINER_APPS_ENVIRONMENT_ID string = aca.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_ID

output aca_AZURE_CONTAINER_REGISTRY_ENDPOINT string = aca.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT

output aca_AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID string = aca.outputs.AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID

output api_identity_id string = api_identity.outputs.id

output postgres_hostName string = postgres.outputs.hostName

output postgres_kv_vaultUri string = postgres_kv.outputs.vaultUri

output storage_blobEndpoint string = storage.outputs.blobEndpoint

output vault_vaultUri string = vault.outputs.vaultUri

output data_protection_key_keyUri string = data_protection_key.outputs.keyUri

output api_identity_clientId string = api_identity.outputs.clientId

output worker_identity_id string = worker_identity.outputs.id

output worker_identity_clientId string = worker_identity.outputs.clientId