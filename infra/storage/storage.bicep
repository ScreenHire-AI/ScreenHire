@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: take('storage${uniqueString(resourceGroup().id)}', 24)
  kind: 'StorageV2'
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    allowSharedKeyAccess: false
    isHnsEnabled: false
    minimumTlsVersion: 'TLS1_2'
    networkAcls: {
      defaultAction: 'Allow'
    }
  }
  tags: {
    'aspire-resource-name': 'storage'
  }
}

resource blobs 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  name: 'default'
  properties: {
    cors: {
      corsRules: [
        {
          allowedOrigins: [
            '*'
          ]
          allowedMethods: [
            'GET'
            'HEAD'
            'PUT'
          ]
          maxAgeInSeconds: 3600
          exposedHeaders: [
            '*'
          ]
          allowedHeaders: [
            '*'
          ]
        }
      ]
    }
  }
  parent: storage
}

resource applicant_uploads 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  name: 'applicant-uploads'
  parent: blobs
}

resource brand_images 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  name: 'brand-images'
  parent: blobs
}

resource email_attachments 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  name: 'email-attachments'
  parent: blobs
}

resource data_exports 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  name: 'data-exports'
  parent: blobs
}

output blobEndpoint string = storage.properties.primaryEndpoints.blob

output dataLakeEndpoint string = storage.properties.primaryEndpoints.dfs

output queueEndpoint string = storage.properties.primaryEndpoints.queue

output tableEndpoint string = storage.properties.primaryEndpoints.table

output name string = storage.name

output id string = storage.id