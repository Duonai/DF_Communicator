# DF_Communicator

## Assets/DynamicFusion/MeshGenerator.cs

- `void MemoryMapping()`
  - Dynamic fusion에서 `vertexMappedFileName`으로 매핑한 데이터에 접근해서 정해진 프로토콜에 따라 <br/>vertex, uv, triangle정보를 받아옵니다.
  - Thread를 사용해 멀티스레드에서 동작하는 while문 무한루프로 dynamic fusion 프로그램으로부터 <br/>갱신 되는 데이터를 주기적으로 읽어옵니다.
 
- `void Processing()`
  - `MemoryMapping`을 수행하여 읽어온 mesh vertex, uv, triangle정보를 unity mesh filter에 넣어주어 <br/>Unity에서 dynamic fusion mesh를 렌더링 합니다.
