@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param aca_outputs_azure_container_apps_environment_default_domain string

param aca_outputs_azure_container_apps_environment_id string

param api_containerimage string

param api_identity_outputs_id string

param api_containerport string

param postgres_outputs_hostname string

@secure()
param screenhire_app_password_value string

param storage_outputs_blobendpoint string

param vault_outputs_vaulturi string

param domains_careers_domain_value string

param domains_base_domain_value string

param domains_api_host_value string

param domains_app_host_value string

param email_from_value string

param email_reply_domain_value string

param sendgrid_event_webhook_public_key_value string

param data_protection_key_outputs_keyuri string

param auth_google_client_id_value string

param auth_microsoft_client_id_value string

param auth_trusted_proxies_value string

param auth_forward_limit_value string

param turnstile_site_key_value string

param slack_client_id_value string

param calendar_google_client_id_value string

param calendar_microsoft_client_id_value string

param google_indexing_mode_value string

param api_identity_outputs_clientid string

param aca_outputs_azure_container_registry_endpoint string

param aca_outputs_azure_container_registry_managed_identity_id string

resource api 'Microsoft.App/containerApps@2025-10-02-preview' = {
  name: 'api'
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
      ingress: {
        external: true
        targetPort: int(api_containerport)
        transport: 'http'
      }
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
          probes: [
            {
              failureThreshold: 3
              httpGet: {
                path: '/alive'
                port: int(api_containerport)
                scheme: 'HTTP'
              }
              initialDelaySeconds: 5
              periodSeconds: 5
              successThreshold: 1
              timeoutSeconds: 1
              type: 'Liveness'
            }
            {
              failureThreshold: 3
              httpGet: {
                path: '/health'
                port: int(api_containerport)
                scheme: 'HTTP'
              }
              initialDelaySeconds: 5
              periodSeconds: 5
              successThreshold: 1
              timeoutSeconds: 1
              type: 'Readiness'
            }
          ]
          image: api_containerimage
          name: 'api'
          env: [
            {
              name: 'OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY'
              value: 'in_memory'
            }
            {
              name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
              value: 'false'
            }
            {
              name: 'HTTP_PORTS'
              value: api_containerport
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
              name: 'ScreenHire__Domains__Scheme'
              value: 'https'
            }
            {
              name: 'ScreenHire__Domains__BaseDomain'
              value: domains_base_domain_value
            }
            {
              name: 'ScreenHire__Domains__ApiHost'
              value: domains_api_host_value
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
              name: 'ScreenHire__Email__SendGrid__EventWebhookPublicKey'
              value: sendgrid_event_webhook_public_key_value
            }
            {
              name: 'ScreenHire__DataProtection__KeyVaultKeyUri'
              value: data_protection_key_outputs_keyuri
            }
            {
              name: 'ScreenHire__Auth__Google__ClientId'
              value: auth_google_client_id_value
            }
            {
              name: 'ScreenHire__Auth__Microsoft__ClientId'
              value: auth_microsoft_client_id_value
            }
            {
              name: 'ScreenHire__Auth__TrustedProxies__0'
              value: auth_trusted_proxies_value
            }
            {
              name: 'ScreenHire__Auth__ForwardLimit'
              value: auth_forward_limit_value
            }
            {
              name: 'ScreenHire__Turnstile__SiteKey'
              value: turnstile_site_key_value
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
              name: 'AZURE_CLIENT_ID'
              value: api_identity_outputs_clientid
            }
            {
              name: 'AZURE_TOKEN_CREDENTIALS'
              value: 'ManagedIdentityCredential'
            }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
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
      '${api_identity_outputs_id}': { }
      '${aca_outputs_azure_container_registry_managed_identity_id}': { }
    }
  }
}