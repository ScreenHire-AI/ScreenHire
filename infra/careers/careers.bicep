@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param aca_outputs_azure_container_apps_environment_default_domain string

param aca_outputs_azure_container_apps_environment_id string

param careers_containerimage string

param domains_careers_domain_value string

param aca_outputs_azure_container_registry_endpoint string

param aca_outputs_azure_container_registry_managed_identity_id string

resource careers 'Microsoft.App/containerApps@2025-07-01' = {
  name: 'careers'
  location: location
  properties: {
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
      }
      registries: [
        {
          server: aca_outputs_azure_container_registry_endpoint
          identity: aca_outputs_azure_container_registry_managed_identity_id
        }
      ]
    }
    environmentId: aca_outputs_azure_container_apps_environment_id
    template: {
      containers: [
        {
          probes: [
            {
              failureThreshold: 3
              httpGet: {
                path: '/'
                port: int('8080')
                scheme: 'HTTP'
              }
              initialDelaySeconds: 5
              periodSeconds: 5
              successThreshold: 1
              timeoutSeconds: 1
              type: 'Readiness'
            }
          ]
          image: careers_containerimage
          name: 'careers'
          env: [
            {
              name: 'PORT'
              value: '8080'
            }
            {
              name: 'HOST'
              value: '0.0.0.0'
            }
            {
              name: 'API_URL'
              value: 'https://api.${aca_outputs_azure_container_apps_environment_default_domain}'
            }
            {
              name: 'ScreenHire__Domains__CareersDomain'
              value: domains_careers_domain_value
            }
            {
              name: 'ScreenHire__Domains__CareersHostPattern'
              value: '{tenant}.{CareersDomain}'
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
        maxReplicas: 2
      }
    }
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${aca_outputs_azure_container_registry_managed_identity_id}': { }
    }
  }
}