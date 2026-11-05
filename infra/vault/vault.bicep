@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

@secure()
param webhook_signing_key_value string

@secure()
param verification_device_hash_key_value string

@secure()
param email_reply_key_value string

@secure()
param sendgrid_inbound_secret_value string

resource vault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: take('vault-${uniqueString(resourceGroup().id)}', 24)
  location: location
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
  }
  tags: {
    'aspire-resource-name': 'vault'
  }
}

resource secret_ScreenHire__Webhooks__SigningKey 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = {
  name: 'ScreenHire--Webhooks--SigningKey'
  properties: {
    value: webhook_signing_key_value
  }
  parent: vault
}

resource secret_ScreenHire__Verification__DeviceHashKey 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = {
  name: 'ScreenHire--Verification--DeviceHashKey'
  properties: {
    value: verification_device_hash_key_value
  }
  parent: vault
}

resource secret_ScreenHire__Email__ReplyKey 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = {
  name: 'ScreenHire--Email--ReplyKey'
  properties: {
    value: email_reply_key_value
  }
  parent: vault
}

resource secret_ScreenHire__Email__SendGrid__InboundSecret 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = {
  name: 'ScreenHire--Email--SendGrid--InboundSecret'
  properties: {
    value: sendgrid_inbound_secret_value
  }
  parent: vault
}

output vaultUri string = vault.properties.vaultUri

output name string = vault.name

output id string = vault.id