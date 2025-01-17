docker rm -f relay_identityserver

docker run `
  --name relay_identityserver `
  --network relay_network `
  -e Serilog__MinimumLevel__Default=Verbose `
  -e Serilog__MinimumLevel__Override__Microsoft=Information `
  -e Serilog__MinimumLevel__Override__Microsoft.Hosting.Lifetime=Information `
  -e Serilog__WriteTo__0__Name=Seq `
  -e Serilog__WriteTo__0__Args__ServerUrl=http://relay_logging_seq `
  -e Serilog__Properties__System=IdentityServer `
  -p 5002:5000 `
  -d `
  relay_identityserver
