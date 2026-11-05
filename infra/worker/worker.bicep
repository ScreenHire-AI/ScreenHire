@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param aca_outputs_azure_container_apps_environment_default_domain string

param aca_outputs_azure_container_apps_environment_id string

param worker_containerimage string

param worker_identity_outputs_id string

param postgres_outputs_hostname string

@secure()
param screenhire_app_password_value string

param storage_outputs_blobendpoint string

param vault_outputs_vaulturi string

param domains_careers_domain_value string

param domains_app_host_value string

param email_from_value string

param email_reply_domain_value string

param data_protection_key_outputs_keyuri string

param slack_client_id_value string

param calendar_google_client_id_value string

param calendar_microsoft_client_id_value string

param google_indexing_mode_value string

param verification_llm_provider_value string

param verification_llm_extraction_model_value string

param verification_llm_consistency_model_value string

param worker_identity_outputs_clientid string

param aca_outputs_azure_container_registry_endpoint string

param aca_outputs_azure_container_registry_managed_identity_id string

resource worker 'Microsoft.App/containerApps@2025-10-02-preview' = {
  name: 'worker'
  location: location
  properties: {
    configuration: {
      secrets: [
        {
          name: 'connectionstrings--screenhire-app'
          value: 'Host=${postgres_outputs_hostname};Database=screenhire;Username=screenhire_app;Password=${screenhire_app_password_value};SSL Mode=Require'
        }
      ]
      activeRevisionsMode: 'Single'
      registries: [
        {
          server: aca_outputs_azure_container_registry_endpoint
          identity: aca_outputs_azure_container_registry_managed_identity_id
        }
      ]
      runtime: {
        dotnet: {
          autoConfigureDataProtection: true
        }
      }
    }
    environmentId: aca_outputs_azure_container_apps_environment_id
    template: {
      containers: [
        {
          image: worker_containerimage
          name: 'worker'
          env: [
            {
              name: 'OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY'
              value: 'in_memory'
            }
            {
              name: 'ConnectionStrings__screenhire-app'
              secretRef: 'connectionstrings--screenhire-app'
            }
            {
              name: 'ConnectionStrings__screenhire_app'
              secretRef: 'connectionstrings--screenhire-app'
            }
            {
              name: 'ConnectionStrings__blobs'
              value: storage_outputs_blobendpoint
            }
            {
              name: 'BLOBS_URI'
              value: storage_outputs_blobendpoint
            }
            {
              name: 'ConnectionStrings__vault'
              value: vault_outputs_vaulturi
            }
            {
              name: 'VAULT_URI'
              value: vault_outputs_vaulturi
            }
            {
              name: 'ScreenHire__Domains__CareersDomain'
              value: domains_careers_domain_value
            }
            {
              name: 'ScreenHire__Domains__CareersHostPattern'
              value: '{tenant}.{CareersDomain}'
            }
            {
              name: 'ScreenHire__Domains__AppHost'
              value: domains_app_host_value
            }
            {
              name: 'ScreenHire__Email__From'
              value: email_from_value
            }
            {
              name: 'ScreenHire__Email__ReplyDomain'
              value: email_reply_domain_value
            }
            {
              name: 'ScreenHire__DataProtection__KeyVaultKeyUri'
              value: data_protection_key_outputs_keyuri
            }
            {
              name: 'ScreenHire__Slack__ClientId'
              value: slack_client_id_value
            }
            {
              name: 'ScreenHire__Calendar__Google__ClientId'
              value: calendar_google_client_id_value
            }
            {
              name: 'ScreenHire__Calendar__Microsoft__ClientId'
              value: calendar_microsoft_client_id_value
            }
            {
              name: 'ScreenHire__Distribution__GoogleIndexing__Mode'
              value: google_indexing_mode_value
            }
            {
              name: 'ScreenHire__Verification__Llm__Provider'
              value: verification_llm_provider_value
            }
            {
              name: 'ScreenHire__Verification__Llm__ExtractionModel'
              value: verification_llm_extraction_model_value
            }
            {
              name: 'ScreenHire__Verification__Llm__ConsistencyModel'
              value: verification_llm_consistency_model_value
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: worker_identity_outputs_clientid
            }
            {
              name: 'AZURE_TOKEN_CREDENTIALS'
              value: 'ManagedIdentityCredential'
            }
          ]
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${worker_identity_outputs_id}': { }
      '${aca_outputs_azure_container_registry_managed_identity_id}': { }
    }
  }
}