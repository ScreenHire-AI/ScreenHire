param vaultName string

resource vault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: vaultName
}

resource key 'Microsoft.KeyVault/vaults/keys@2024-11-01' = {
  parent: vault
  name: 'data-protection'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: [
      'wrapKey'
      'unwrapKey'
    ]
  }
}

output keyUri string = key.properties.keyUri