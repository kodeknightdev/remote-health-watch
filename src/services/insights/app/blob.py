from typing import Protocol

from azure.core.exceptions import ResourceExistsError
from azure.storage.blob import BlobServiceClient, ContentSettings


class BlobStore(Protocol):
    def put(self, name: str, data: bytes, content_type: str) -> None: ...
    def get(self, name: str) -> bytes: ...


class AzureBlobStore:
    """Azurite locally, Azure Storage in the cloud; only the connection string changes."""

    def __init__(self, connection_string: str, container: str):
        self.service = BlobServiceClient.from_connection_string(connection_string)
        self.container = container
        self._ready = False

    def _client(self):
        client = self.service.get_container_client(self.container)
        if not self._ready:
            try:
                client.create_container()
            except ResourceExistsError:
                pass
            self._ready = True
        return client

    def put(self, name: str, data: bytes, content_type: str) -> None:
        self._client().upload_blob(
            name, data, overwrite=True, content_settings=ContentSettings(content_type=content_type)
        )

    def get(self, name: str) -> bytes:
        return self._client().download_blob(name).readall()

    def ping(self) -> None:
        self.service.get_account_information()


class MemoryBlobStore:
    def __init__(self):
        self.blobs: dict[str, tuple[bytes, str]] = {}

    def put(self, name: str, data: bytes, content_type: str) -> None:
        self.blobs[name] = (data, content_type)

    def get(self, name: str) -> bytes:
        return self.blobs[name][0]
