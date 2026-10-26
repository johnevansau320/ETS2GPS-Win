SIIDecryptSharp by Joshua Menzel (https://gitlab.com/jammerxd/sii-decryptsharp), as vendored in the TruckSim GPS server
(https://github.com/TruckSim-GPS/trucksim-gps-server, source/Funbit.Ets.Telemetry.Server/SiiDecrypt), licence in LICENSE.
Changes for ETS2 GPS: "#nullable disable" at the top of each file, and value type 0x41 is skipped in BSII_Decoder.cs.
Used only to read the player's own saves (the game GPS destination); save files are never written.
